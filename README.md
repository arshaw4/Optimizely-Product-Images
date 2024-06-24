This C# program uses Optimizelys B2B Configured Commerce v1 Rest api calls: https://docs.developers.optimizely.com/configured-commerce/reference/getting-started-with-the-b2b-commerce-rest-apis
Using an administrator login it gets all products including discontinued products, but not archived products, and checks if they are missing an image
or if the current image for the product is unreachable (ie. referenced an external source that is no longer available)
The program then adds the product, erpNumber, id, image link, and product link to an array and outputs it to a tab delimited text file which can be easily viewed in excel

Read this for information on how the Program.GetToken() function works: https://docs.developers.optimizely.com/configured-commerce/reference/admin-api-architecture
