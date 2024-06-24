/*using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace NoImageCheck
{
    class ProgramOld
    {
        //This is the old version, discovered that the non-admin api calls do not include "discontinued products"
        //We need discontinued products because occasionally they are readded to the site
        //Rewrote program much more simply because I only need 1 api call to get all products from the website including discontinued products
        //Takes about 80-100 seconds but it will be more efficient than going by category and requires way less API calls that have a chance of rate limiting
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(20, 20);

        static HashSet<string> encounteredProdIDs = new HashSet<string>();
        static object lockObject = new object();
        static int counter = 0;
        static int noCategoryCounter = 0;
        static int flagged = 0;
        static int totalCountFromPagination = 0;

        static Stopwatch timer;
        static JArray itemsMissingImg;
        //static SpreadsheetInfoStruct[] spreadsheetInfo = new SpreadsheetInfoStruct[];
        static void MainOld(string[] args)
        {
            timer = new Stopwatch();
            timer.Start();
            itemsMissingImg = new JArray();

            JObject rootCategories = GetCategoryList();
            IterateCategories((JArray)rootCategories["categories"]);
            GetProductsNoCategory();

            using (StreamWriter sw = new StreamWriter(Settings.savePath))
            {
                sw.WriteLine("erpNumber" + "\t" + "id" + "\t" + "errorMessage" + "\t" + "imgURL" + "\t" + "prodURL");
                foreach (var item in itemsMissingImg)
                {
                    if (item != null)
                    {
                        string newLine = item["erpNumber"] + "\t" + item["id"] + "\t" + item["errorMessage"] + "\t" + item["imgURL"] + "\t" + item["prodURL"] + "\t";
                        sw.WriteLine(newLine);
                    }
                }
                sw.Close();
            }

            timer.Stop();
            Console.WriteLine("Time to process product catalog from ecommsite: " + timer.Elapsed.Duration().ToString());
            Console.WriteLine("Total Products pulled from Ecomm ->" + counter.ToString());
            Console.WriteLine("Total Products with no Category ->" + noCategoryCounter.ToString());
            Console.WriteLine("Total flaggged URLs ->" + flagged.ToString());
            Console.WriteLine("Pagination Total ->" + totalCountFromPagination.ToString());
        }

        static void IterateCategories(JArray categories)
        {
            Parallel.ForEach(categories, category =>
            {
                string id = category["id"].ToString();
                if (id != string.Empty)
                {
                    if (id as string != string.Empty)
                    {
                        var guid = (string)id;
                        var defaultCategoryInfo = DefaultCategoryProductInfo(guid);
                        if (defaultCategoryInfo != null)
                        {
                            int numberOfPages = (int)defaultCategoryInfo["pagination"]["numberOfPages"];
                            totalCountFromPagination += (int)defaultCategoryInfo["pagination"]["totalItemCount"];
                            Parallel.For(1, numberOfPages + 1, pageNum =>
                            {
                                JToken products = GetProducts(guid, pageNum);
                                if (products != null)
                                {
                                    foreach (var product in products)
                                    {
                                        Boolean itemExists = false;
                                        lock (lockObject)
                                        {
                                            string erpNumber = (string)product["erpNumber"];
                                            if (encounteredProdIDs.Contains(erpNumber))
                                            {
                                                itemExists = true;
                                            }
                                            else
                                            {
                                                encounteredProdIDs.Add(erpNumber);
                                            }
                                        }
                                        if (!itemExists)
                                        {
                                            counter++;
                                            var productImages = product["productImages"];
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
                                                    Parallel.ForEach(URLs, async url =>
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
                                                                //"...prodnotavail.jpg"
                                                                string[] keywords = { "notavailable", "unavailable", "error", "notavail", "open-box-icon-symbol-vector" };
                                                                string pattern = @"(" + string.Join("|", keywords.Select(Regex.Escape)) + @")";
                                                                Regex regex = new Regex(pattern, RegexOptions.IgnoreCase);
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
                                                                            HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));

                                                                            if (!response.IsSuccessStatusCode)
                                                                            {
                                                                                AddMissingItem(product, "Image Moved", url);
                                                                            }
                                                                        }
                                                                        catch (TaskCanceledException ex)
                                                                        {
                                                                            //timedout
                                                                            Console.WriteLine(ex.Message);
                                                                            AddMissingItem(product, "Image Timeout", url);
                                                                        }
                                                                        catch (HttpRequestException ex)
                                                                        {
                                                                            Console.WriteLine($"An error occured: {ex.Message}");
                                                                        }
                                                                    }

                                                                    *//*var pingTask = pingResourceAsync(url);
                                                                    string error = await pingTask;
                                                                    if (error != null)
                                                                    {
                                                                        AddMissingItem(product, error, url);
                                                                    }*//*
                                                                }
                                                            }
                                                        }


                                                    });
                                                });
                                            }

                                        }
                                    }
                                }
                            });
                        }
                    }
                }
                JToken cat = category["subCategories"];

                if (cat.HasValues == true)
                {
                    IterateCategories((JArray)category["subCategories"]);
                }
            });
        }

        public static async Task<string> pingResourceAsync(string url)
        {
            await semaphore.WaitAsync();
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                using (HttpClient client = new HttpClient())
                {
                    try
                    {
                        HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));

                        if (!response.IsSuccessStatusCode)
                        {
                            return "Image Moved";
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        //timedout
                        return "Image Timeout";
                    }
                    catch (HttpRequestException ex)
                    {
                        return "An error occured: " + ex.Message;
                    }
                }
                //no error
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        }

        static void GetProductsNoCategory()
        {
            var guid = "";
            var defaultCategoryInfo = DefaultCategoryProductInfo(guid);
            if (defaultCategoryInfo != null)
            {
                int numberOfPages = (int)defaultCategoryInfo["pagination"]["numberOfPages"];
                totalCountFromPagination += (int)defaultCategoryInfo["pagination"]["totalItemCount"];
                Parallel.For(1, numberOfPages + 1, pageNum =>
                {
                    JToken products = GetProducts(guid, pageNum);
                    if (products != null)
                    {
                        foreach (var product in products)
                        {
                            Boolean itemExists = false;
                            lock (lockObject)
                            {
                                string erpNumber = (string)product["erpNumber"];
                                if (encounteredProdIDs.Contains(erpNumber))
                                {
                                    itemExists = true;
                                }
                                else
                                {
                                    encounteredProdIDs.Add(erpNumber);
                                }
                            }
                            if (!itemExists)
                            {
                                counter++;
                                noCategoryCounter++;
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
                                        Parallel.ForEach(URLs, async url =>
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
                                                                HttpResponseMessage response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));

                                                                if (!response.IsSuccessStatusCode)
                                                                {
                                                                    AddMissingItem(product, "Image Moved", url);
                                                                }
                                                            }
                                                            catch (TaskCanceledException ex)
                                                            {
                                                                //timedout
                                                                Console.WriteLine(ex.Message);
                                                                AddMissingItem(product, "Image Timeout", url);
                                                            }
                                                            catch (HttpRequestException ex)
                                                            {
                                                                Console.WriteLine($"An error occured: {ex.Message}");
                                                            }
                                                        }

                                                        *//*var pingTask = pingResourceAsync(url);
                                                        string error = await pingTask;
                                                        if (error != null)
                                                        {
                                                            AddMissingItem(product, error, url);
                                                        }*//*
                                                    }
                                                }
                                            }


                                        });
                                    });
                                }

                            }
                        }
                    }
                });
            }
        }

        static void AddMissingItem(JToken product, string errorMessage, string flaggedURL)
        {
            //if there are multiple flagged images for a single product, and I modify the product
            //JToken for the first flagged URL, will the next flagged URL start from the original
            //product JSON or will it add after the first flagged url
            //What I want:
            //product1 URL1 error
            //product1 URL2 error
            //what might happen
            //product1 URL1 error
            //product1 URL1 error URL2 error
            flagged++;
            SpreadsheetInfoStruct spreadsheetInfoStruct = new SpreadsheetInfoStruct();
            spreadsheetInfoStruct.erpNumber = (string)product["erpNumber"];
            spreadsheetInfoStruct.id = (string)product["id"];
            spreadsheetInfoStruct.errorMessage = errorMessage;
            spreadsheetInfoStruct.imgURL = flaggedURL;
            spreadsheetInfoStruct.prodURL = Settings.baseURL + (string)product["productDetailUrl"];
            *//*            var flaggedURLProp = new JProperty("flaggedURL", flaggedURL);
                        var errorMessageProp = new JProperty("errorMessage", errorMessage);
                        product.Last.AddAfterSelf(flaggedURLProp);
                        product.Last.AddAfterSelf(errorMessageProp);*//*
            itemsMissingImg.Add(JObject.Parse(JsonConvert.SerializeObject(spreadsheetInfoStruct)));

            *//*Console.Write(product["erpNumber"]);
            Console.Write(" " + errorMessage);
            Console.WriteLine(" " + flaggedURL);*//*
        }

        static JToken GetProducts(string guid, int pageNum)
        {
            *//*            var client = new RestClient(StoreFrontAPI.Products);
                        var request = new RestRequest(StoreFrontAPI.Products, Method.Get);

                        request.AddHeader("Accept", "application/json");
                        request.AddHeader("Content-Type", "text/json");

                        request.AddQueryParameter("categoryId", guid);
                        request.AddQueryParameter("page", pageNum);

                        RestResponse response = client.Execute(request);

                        if (response.Content != null)
                        {
                            if (response.IsSuccessful)
                            {
                                JObject productsJSON = JsonConvert.DeserializeObject<JObject>(response.Content);
                                return productsJSON["products"];
                            }
                        }

                        return null;*//*

            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage responseMes = null;
                string response = "";
                Boolean timeOut = true;
                //time in ms will double every failure
                Int32 sleepTime = 100;
                while (timeOut)
                {
                    try
                    {
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, StoreFrontAPI.Products + "?categoryID=" + guid + "&page=" + pageNum);
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
                        Console.WriteLine("Retrying Product Page Call");
                        Console.WriteLine("Sleeping for " + sleepTime.ToString());
                    }
                }


                if (response != null)
                {
                    if (responseMes.IsSuccessStatusCode)
                    {
                        JObject productsJSON = JObject.Parse(response);
                        return productsJSON["products"];
                    }
                }

                return null;
            }
        }

        static JObject DefaultCategoryProductInfo(string categoryID)
        {
            *//*            var client = new RestClient(StoreFrontAPI.Products);
                        var request = new RestRequest(StoreFrontAPI.Products, Method.Get);

                        request.AddHeader("Accept", "application/json");
                        request.AddHeader("Content-Type", "text/json");

                        request.AddQueryParameter("categoryId", categoryID);

                        RestResponse response = client.Execute(request);

                        if (response.Content != null)
                        {
                            if (response.IsSuccessful)
                            {
                                JObject productsJSON = JsonConvert.DeserializeObject<JObject>(response.Content);
                                return productsJSON;
                            }
                        }

                        return null;*//*

            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage responseMes = null;
                string response = "";
                Boolean timeOut = true;
                Int32 sleepTime = 100;
                while (timeOut)
                {
                    try
                    {
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, StoreFrontAPI.Products + "?categoryID=" + categoryID);
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
                        Console.WriteLine("Retrying Category Info Call");
                        Console.WriteLine("Sleeping for " + sleepTime.ToString());
                    }
                }



                if (response != null)
                {
                    if (responseMes.IsSuccessStatusCode)
                    {
                        JObject productsJSON = JObject.Parse(response);
                        return productsJSON;
                    }
                }

                return null;
            }
        }

        static JObject GetCategoryList()
        {
            *//*            var client = new RestClient(StoreFrontAPI.Categories);
                        var request = new RestRequest(StoreFrontAPI.Categories, Method.Get);

                        request.AddHeader("Accept", "application/json");
                        request.AddHeader("Content-Type", "text/json");

                        request.AddQueryParameter("maxDepth", 20);

                        RestResponse response = client.Execute(request);*//*
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage responseMes = null;
                string response = "";
                Boolean timeOut = true;
                Int32 sleepTime = 100;
                while (timeOut)
                {
                    try
                    {
                        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, StoreFrontAPI.Categories + "?maxDepth=20");
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
                        Console.WriteLine("Retrying Categories call");
                        Console.WriteLine("Sleeping for " + sleepTime.ToString());
                    }
                }


                if (response != null)
                {
                    if (responseMes.IsSuccessStatusCode)
                    {
                        JObject JSON = JObject.Parse(response);
                        //JObject JSON = JsonConvert.DeserializeObject<JObject>(response.Content);

                        return JSON;
                    }
                }

                return null;
            }
        }
    }
}*/