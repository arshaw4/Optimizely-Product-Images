using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

namespace NoImageCheck
{
    class Program
    {
        static int counter = 0;
        static int flagged = 0;
        static object missingLock = new object();
        static object counterLock = new object();

        static Stopwatch timer;
        static JArray itemsMissingImg;
        static Dictionary<string, string> brands;

        static void Main(string[] args)
        {
            timer = new Stopwatch();
            timer.Start();
            itemsMissingImg = new JArray();
            brands = new Dictionary<string, string>();

            string token = GetToken();
            GetAllProducts(token);

            using (StreamWriter sw = new StreamWriter(Settings.savePath))
            {
                sw.WriteLine("erpNumber" + "\t" + "brand"+"\t"+"id" + "\t" + "errorMessage" + "\t" + "imgURL" + "\t" + "prodURL" + "\t" + "discontinued" + "\t" + "admin product URL");
                foreach (var item in itemsMissingImg)
                {
                    if (item != null)
                    {
                        string newLine = item["erpNumber"] + "\t" + item["brand"] +"\t" + item["id"] + "\t" + item["errorMessage"] + "\t" + item["imgURL"] + "\t" + item["prodURL"] + "\t" + item["isDiscontinued"] +"\t" + Settings.ProductsDash + item["id"] +"\t";
                        sw.WriteLine(newLine);
                    }
                }
                sw.Close();
            }

            timer.Stop();
            Console.WriteLine("Time to process product catalog from ecommsite: " + timer.Elapsed.Duration().ToString());
            Console.WriteLine("Total Products pulled from Ecomm ->" + counter.ToString());
            Console.WriteLine("Total flaggged URLs ->" + flagged.ToString());
        }

        static void GetAllProducts(string token)
        {
            GetAndStoreBrands(token);
            JToken fullProductList = QueryAPIAllProducts(token);
            Parallel.ForEach(fullProductList, product =>
            {
                lock (counterLock)
                {
                    counter++;
                }
                var productImages = product["productImages"];
                //check if there is no image at all like:
                //"https://www.northeastern.com/Catalog/tools-safety-supplies/-job-site-supplies/Milwaukee-Q2-QP-PACKOUT-DISPLAY-2024-MIL58-28-0273-2575708"
                //there should be no url in the API, but it is possible it could be something like this:
                //https://d3sahnt5l4coo5.cloudfront.net/userfiles/open-box-icon-symbol-vector-12291103.jpg
                if (productImages == null || productImages.Count() == 0)
                {
                    AddMissingItem(product, "No Image", "");
                }
                else
                {
                    //there can be multiple images for each product so we want to check every image
                    Parallel.ForEach(productImages, image =>
                    {
                        //although not commonly used on the website theres a possibility for each image
                        //to have different links based on the size at which its being display
                        string[] URLs = new string[3];
                        URLs[0] = (string)image["smallImagePath"];
                        URLs[1] = (string)image["mediumImagePath"];
                        URLs[2] = (string)image["largeImagePath"];
                        string distinctURL = "";
                        //Having issues with flagging duplicte image links so this loop gets rid of duplicates
                        for (int i = 0; i < URLs.Length; i++)
                        {
                            if (Regex.IsMatch(URLs[i].ToLower(), "^\\/userfiles/"))
                            {
                                URLs[i] = Settings.baseURL + URLs[i];
                            }
                            if(URLs[i] != "")
                            {
                                if (distinctURL == "")
                                {
                                    distinctURL = URLs[i];
                                }
                                else if(URLs[i].Equals(distinctURL))
                                {
                                    URLs[i] = "";
                                }
                            }
                        }
                        Parallel.ForEach(URLs, url =>
                        {
                            if (!string.IsNullOrEmpty(url))
                            {
                                //check if url is a valid url
                                if (!Regex.IsMatch(url, @"^https?://"))
                                {
                                    AddMissingItem(product, "URL Format Error", url);
                                }
                                else
                                {
                                    //do regex to check if url uses anything like:
                                    //"http://images.tradeservice.com/PRODUCTIMAGES/DIR100000/NEPRODNOTAVAIL.jpg"
                                    //"...prodnotavail.jpg"
                                    //keyword array of common urls for missing images 
                                    string[] keywords = { "notavailable", "unavailable", "error", "notavail" };
                                    string pattern = @"(" + string.Join("|", keywords.Select(Regex.Escape)) + @")";
                                    Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);
                                    //Console.WriteLine(url);
                                    if (regex.IsMatch(url))
                                    {
                                        AddMissingItem(product, "Generic Image", url);
                                    }
                                    //if regex does not detect a bad url than http request to see if image doesn't exist anymore
                                    else
                                    {

                                        //do http request

                                        //if it is not successful (check for timeout, forbidden, 404, any kind of non-success reponse) in finding image the image is invalid
                                        //example from website of product with image that doesn't exist anymore
                                        //https://www.northeastern.com/Catalog/tools-safety-supplies/-job-site-supplies/Cooper-Wiring-Devices-120-VAC-15-A-1-pole-Screw-Terminal-Ivory-Polycarbonate-Standard-Grade-Toggle-Switch-1301-7V-30546


                                        using (HttpClient client = new HttpClient())
                                        {
                                            try
                                            {
                                                //Request timesout if no response after 15 seconds
                                                client.Timeout = TimeSpan.FromSeconds(15);
                                                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url);
                                                HttpResponseMessage response = client.Send(request);
                                                response.EnsureSuccessStatusCode();
                                                var trashValue = response.Content.ReadAsStringAsync().Result;
                                            }
                                            catch (TaskCanceledException ex)
                                            {
                                                AddMissingItem(product, "Image Timeout", url);
                                            }
                                            catch (HttpRequestException ex)
                                            {
                                                AddMissingItem(product,"Image Moved", url);
                                            }
                                        }
                                    }
                                }
                            }


                        });
                    });
                }

            });
        }

        static void GetAndStoreBrands(string token)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage responseMes = null;
                string response = "";
                Boolean timeOut = true;
                //time in ms will double every failure
                Int32 sleepTime = 10000;
                while (timeOut)
                {
                    try
                    {
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Settings.Brands + "?=&archiveFilter=0&$count=true&$select=id,name");
                        request.Headers.Add("authorization", "Bearer " + token);
                        responseMes = client.Send(request);
                        responseMes.EnsureSuccessStatusCode();
                        response = responseMes.Content.ReadAsStringAsync().Result;
                        timeOut = false;
                    }
                    catch (Exception ex)
                    {
                        timeOut = true;
                        Thread.Sleep(sleepTime);
                        sleepTime *= 2;
                        Console.WriteLine(ex.Message);
                        Console.WriteLine("Retrying Product Call");
                        Console.WriteLine("Sleeping for " + sleepTime.ToString());
                    }
                }


                if (response != null)
                {
                    if (responseMes.IsSuccessStatusCode)
                    {
                        JObject brandsJSON = JObject.Parse(response);
                        Console.WriteLine("Found " + brandsJSON["@odata.count"] + " Brands");
                        foreach(var brand in brandsJSON["value"])
                        {
                            brands.Add((string)brand["id"], (string)brand["name"]);
                        }
                    }
                }
            }
        }

        static JToken QueryAPIAllProducts(string token)
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage responseMes = null;
                string response = "";
                Boolean timeOut = true;
                //time in ms will double every failure
                Int32 sleepTime = 10000;
                while (timeOut)
                {
                    try
                    {
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, Settings.Products + "?=&archiveFilter=0&$count=true&$orderby=erpNumber&$expand=productImages&$select=id,erpNumber,urlSegment,brandId,isDiscontinued");
                        request.Headers.Add("authorization", "Bearer " + token);
                        responseMes = client.Send(request);
                        responseMes.EnsureSuccessStatusCode();
                        response = responseMes.Content.ReadAsStringAsync().Result;
                        timeOut = false;
                    }
                    catch (Exception ex)
                    {
                        timeOut = true;
                        Thread.Sleep(sleepTime);
                        sleepTime *= 2;
                        Console.WriteLine(ex.Message);
                        Console.WriteLine("Retrying Product Call");
                        Console.WriteLine("Sleeping for " + sleepTime.ToString());
                    }
                }


                if (response != null)
                {
                    if (responseMes.IsSuccessStatusCode)
                    {
                        JObject productsJSON = JObject.Parse(response);
                        Console.WriteLine("Found " + productsJSON["@odata.count"] + " Products");
                        return productsJSON["value"];
                    }
                }

                return null;
            }
        }

        private static string GetToken()
        {
            IdentityStruct identStruct = new IdentityStruct();
            identStruct.grant_type = "password";
            identStruct.username = "admin_username";
            identStruct.password = "password";
            identStruct.scope = "isc_admin_api offline_access";

            using (HttpClient client = new HttpClient())
            {
                //it is unlikely but this handles a timeout or rate limit on single token request
                HttpResponseMessage responseMes = null;
                Boolean timeOut = true;
                Int32 sleepTime = 100;
                Int32 maxSleepTime = 10000;
                while (timeOut)
                {
                    try
                    {
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Settings.identURL);
                        request.Headers.Add("authorization", Settings.authForToken);
                        request.Content = new StringContent(identStruct.ToString());
                        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");
                        responseMes = client.Send(request);
                        responseMes.EnsureSuccessStatusCode();
                        JObject JSON = JObject.Parse(responseMes.Content.ReadAsStringAsync().Result);
                        string token = (string)JSON["access_token"];
                        timeOut = false;
                        return token;
                    }
                    catch (Exception e)
                    {
                        if ((int)responseMes.StatusCode == 400)
                        {
                            Console.WriteLine("Incorrect Credentials");
                            Environment.Exit(1);
                        }
                        if (sleepTime > maxSleepTime)
                        {
                            Console.WriteLine("Fatal Error: API unresponsive");
                            Environment.Exit(1);
                        }
                        timeOut = true;
                        Console.Write("Token Request Failed: ");
                        Console.WriteLine(e.Message);
                        Console.WriteLine("Retrying in " + (sleepTime / 1000).ToString() + "seconds");
                        Thread.Sleep(sleepTime);
                        sleepTime *= 2;
                    }

                }
                return null;
            }
        }

        static string GetBrandName(string brandId)
        {
            return brands.GetValueOrDefault(brandId);
        }

        static void AddMissingItem(JToken product, string errorMessage, string flaggedURL)
        {
            SpreadsheetInfoStruct spreadsheetInfoStruct = new SpreadsheetInfoStruct();
            spreadsheetInfoStruct.erpNumber = (string)product["erpNumber"];
            string brandId = (string)product["brandId"];
            if (brandId != null)
            {
                spreadsheetInfoStruct.brand = GetBrandName(brandId);
            }
            else
            {
                spreadsheetInfoStruct.brand = "";
            }
            spreadsheetInfoStruct.id = (string)product["id"];
            spreadsheetInfoStruct.errorMessage = errorMessage;
            spreadsheetInfoStruct.imgURL = flaggedURL;
            spreadsheetInfoStruct.prodURL = Settings.baseURL + "/Product/" + (string)product["urlSegment"];
            spreadsheetInfoStruct.isDiscontinued = (string)product["isDiscontinued"];
            lock (missingLock)
            {
                flagged++;
                itemsMissingImg.Add(JObject.Parse(JsonConvert.SerializeObject(spreadsheetInfoStruct)));
            }
        }
    }
}