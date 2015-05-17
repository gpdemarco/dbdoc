namespace ConsoleApplication1
{
    using Microsoft.Azure.Documents;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml;
    using Wellhub;
    

    class Program
    {
        public static void Main(string[] args)
        {

            try
            {
                //RunAddDocAsync();
                //RunDelDocAsync();
                //RunDelBatchAsync();
                //RunAddBatchAsync();
                //RunGetDocByID();
                //RunGetDocFeedAsync();
                //RunReplaceDocAsync();
                RunReplaceBatchAsync();

            }
            catch (Exception)
            {
                throw;
            }
        }

        private static async Task RunDelBatchAsync()
        {
            try
            {
                DocHandler handler = new DocHandler();
                string[] batch = 
                    { "6f654eb7-569c-4de9-95c6-0bda12417f4b",
                    "84424d92-a98e-4a36-9cfc-cfa8a9a8d43b",
                    "2e6dbab8-cbce-4643-8fdc-9d7ca9ba2bbe",
                    "605e8af8-67f6-41e9-a04a-50d871e18cb7",
                    "e4346009-05a4-4852-85fc-371cab3ca39e",
                    "674995f0-a42e-4d24-8f66-2304e4a20385"
                };
                Task<List<WHResponse>> taskTest = handler.DeleteBatchAsync(batch.ToList());
                Task.WaitAll(taskTest);
                List<WHResponse> retInt = taskTest.Result;
            }
            catch (Exception)
            {

                throw;
            }
        }
        private static async Task RunDelDocAsync()
        {
            try
            {
                DocHandler handler = new DocHandler();
                Task<WHResponse> taskTest = handler.DeleteDocAsync("f80aa2b3-40fc-4725-b9cf-68f46ad819ab");
                Task.WaitAll(taskTest);
                WHResponse retObj = taskTest.Result;
            }
            catch (Exception)
            {

                throw;
            }
        }
        private static async Task RunAddDocAsync()
        {
            try
            {
                //POCO test
                dynamic docDB = new
                {
                    teststring1 = "AddDocAsync POCO testing",
                    teststring2 = DateTime.Now.ToUniversalTime()
                };
                DocHandler handler = new DocHandler();
                Task<WHResponse> taskTest = handler.AddDocAsync(docDB);
                Task.WaitAll(taskTest);
                WHResponse respObj = taskTest.Result;

                //string test
                string strJson = string.Concat("{ 'AddDocAsync string testing' : '", DateTime.Now, "'}");
                taskTest = handler.AddDocAsync(strJson);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

                //string test - fails - empty string
                strJson = "{ }";
                taskTest = handler.AddDocAsync(strJson);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

                //document test
                strJson = string.Concat("{ 'AddDocAsync document testing' : '", DateTime.Now, "'}");
                Document doc = new Document();
                doc.LoadFrom(new JsonTextReader(new StringReader(strJson)));
                taskTest = handler.AddDocAsync(doc);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

                //jobject test
                strJson = string.Concat("{ 'AddDocAsync jobject testing' : '", DateTime.Now, "'}");
                JObject jObj = JObject.Parse(strJson);
                taskTest = handler.AddDocAsync(jObj);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

                //xml document test
                XmlDocument docXML = new XmlDocument();
                docXML.InnerXml = string.Concat("<testroot><firstField>XML doc test</firstField><secondField>", DateTime.Now, "</secondField></testroot>");
                taskTest = handler.AddDocAsync(docXML);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

                //xml string test
                string strXml = string.Concat("<testroot><firstField>XML string test</firstField><secondField>", DateTime.Now, "</secondField></testroot>");
                taskTest = handler.AddDocAsync(strXml);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

                //xml string test - fails - bad xml
                strXml = string.Concat("<testroot><firstField>XML string test<firstField><secondField>", DateTime.Now, "</secondField></testroot>");
                taskTest = handler.AddDocAsync(strXml);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

                //POCO test - fails - duplicate ID
                docDB = new
                {
                    id = "94a23e36-8xyz-45ce-9189-8624e7e174b9",
                    teststring1 = "AddDocAsync POCO testing - fails",
                    teststring2 = DateTime.Now.ToUniversalTime()
                };
                taskTest = handler.AddDocAsync(docDB);
                Task.WaitAll(taskTest);
                respObj = taskTest.Result;

            }
            catch (Exception)
            {
                throw;
            }
        }
        private static async Task RunAddBatchAsync()
        {
            try
            {
                //POCO test
                var docsDB = new List<object>();
                docsDB.Add(new
                {
                    teststring1 = "AddBatchAsync POCO testing obj1",
                    teststring2 = DateTime.Now
                });
                docsDB.Add(new
                {
                    teststring1 = "AddBatchAsync POCO testing obj2",
                    teststring2 = DateTime.Now
                });
                DocHandler handler = new DocHandler();
                Task<List<WHResponse>> taskTest = handler.AddBatchAsync(docsDB);
                Task.WaitAll(taskTest);
                List<WHResponse> respList = taskTest.Result;

                //string test
                List<string> listJson = new List<string>();
                listJson.Add("{  }");
                listJson.Add(string.Concat("{ 'AddBatchAsync string testing 1' : '", DateTime.Now, "'}"));
                listJson.Add(string.Concat("{ 'AddBatchAsync string testing 2' : '", DateTime.Now, "'}"));
                listJson.Add(string.Concat("{ 'AddBatchAsync string testing 3' : '", DateTime.Now, "'}"));
                taskTest = handler.AddBatchAsync(listJson);
                Task.WaitAll(taskTest);
                respList = taskTest.Result;

                ////document test
                //strJson = string.Concat("{ 'AddDocAsync document testing' : '", DateTime.Now, "'}");
                //Document doc = new Document();
                //doc.LoadFrom(new JsonTextReader(new StringReader(strJson)));
                //taskTest = handler.AddDocAsync(doc);
                //Task.WaitAll(taskTest);
                //respList = taskTest.Result;

                ////jobject test
                //strJson = string.Concat("{ 'AddDocAsync jobject testing' : '", DateTime.Now, "'}");
                //JObject jObj = JObject.Parse(strJson);
                //taskTest = handler.AddDocAsync(jObj);
                //Task.WaitAll(taskTest);
                //respList = taskTest.Result;

                ////xml document test
                //XmlDocument docXML = new XmlDocument();
                //docXML.InnerXml = string.Concat("<testroot><firstField>XML doc test</firstField><secondField>", DateTime.Now, "</secondField></testroot>");
                //taskTest = handler.AddDocAsync(docXML);
                //Task.WaitAll(taskTest);
                //respList = taskTest.Result;

                //// stream test
                //docXML.GetElementsByTagName("firstField").Item(0).InnerText = "Streaming test";
                //docXML.GetElementsByTagName("secondField").Item(0).InnerText = DateTime.Now;
                //MemoryStream testStream = new MemoryStream();
                //docXML.Save(testStream);
                //taskTest = handler.AddDocAsync(testStream);
                //Task.WaitAll(taskTest);
                //respList = taskTest.Result;

                ////xml string test
                //string strXml = string.Concat("<testroot><firstField>XML string test</firstField><secondField>", DateTime.Now, "</secondField></testroot>");
                //taskTest = handler.AddDocAsync(strXml);
                //Task.WaitAll(taskTest);
                //respList = taskTest.Result;
            }
            catch (Exception)
            {
                throw;
            }
        }
        private static void RunGetDocByID()
        {
            DocHandler handler = new DocHandler();
            WHResponse respObj = handler.GetDocByID("94a23e36-8ba1-45ce-9189-8624e7e174b9");

            respObj = handler.GetDocByID("94a23e36-8ba1-45ce-9189-8624e7e174b9", DocHandler.ReturnType.XMLstring);
        }
        private static void RunGetDocFeedAsync()
        {
            DocHandler handler = new DocHandler();
            Task<WHResponse> taskTest = handler.GetDocsAsync("SELECT * FROM Docs d WHERE d.id !='94a23e36-8ba1-45ce-9189-8624e7e174b9'",DocHandler.ReturnType.JSONstring, 20);
            Task.WaitAll(taskTest);
            WHResponse respObj = taskTest.Result;

            taskTest = handler.GetDocsAsync("SELECT * FROM Docs d WHERE d.id !='674995f0-a42e-4d24-8f66-2304e4a20385'", DocHandler.ReturnType.XMLstring, 5);
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;

            taskTest = handler.GetDocsAsync("SELECT * FROM Docs d WHERE d.id !='674995f0-a42e-4d24-8f66-2304e4a20385'", DocHandler.ReturnType.XMLstring, 8, null, respObj.Continuation);
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;

            Expression<Func<Document, bool>> lambdaExp = d => d.Id == "674995f0-a42e-4d24-8f66-2304e4a20385";
            taskTest = handler.GetDocsAsync(lambdaExp, DocHandler.ReturnType.XMLstring, 3, null, respObj.Continuation);
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;
        }
        private static void RunReplaceDocAsync()
        {
            //regular replace
            DocHandler handler = new DocHandler();
            string savedID = "94a23e36-8xyz-45ce-9189-8624e7e174b9";
            WHResponse respObj = handler.GetDocByID(savedID);
            JObject code = JObject.Parse(respObj.Return);
            code["checking3"] = DateTime.Now;
            Task<WHResponse> taskTest = handler.ReplaceDocAsync(code, respObj.Link);
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;

            //replace with no self-id - fails because document is not type Document
            code["checking3"] = DateTime.Now;
            taskTest = handler.ReplaceDocAsync(code);
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;

            //replace with no self-id - fails because document is not type Document
            code["checking3"] = DateTime.Now;
            Document newDoc = code.ToObject<Document>();
            taskTest = handler.ReplaceDocAsync(newDoc);
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;

            //replace with conflict - should fail - id is taken by another document
            code["checking3"] = 111;
            code["id"] = "ead28a30-7104-47c5-8691-eef70dad61f2";
            taskTest = handler.ReplaceDocAsync(code, respObj.Link);
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;

            //replace with bad selfID - should fail 
            code["id"] = savedID;
            taskTest = handler.ReplaceDocAsync(code, "dbs/EPNLAA==/colls/EPNLAOgLWAA=/docs/EPNLAOgLWAAHAAAAAAAAAA=6/");
            Task.WaitAll(taskTest);
            respObj = taskTest.Result;
        }
        private static void RunReplaceBatchAsync()
        {
            //regular replace
            DocHandler handler = new DocHandler();
            List<Document> newDocs = new List<Document>();
            string savedID = "a012d1c9-398a-48da-95a7-d3f5f94e4d69";
            WHResponse respObj = handler.GetDocByID(savedID);
            JObject code = JObject.Parse(respObj.Return);
            code["replbatch"] = DateTime.Now;
            newDocs.Add(code.ToObject<Document>());

            savedID = "8324db59-dbce-4804-b0af-3f774551c35a";
            respObj = handler.GetDocByID(savedID);
            code = JObject.Parse(respObj.Return);
            code["replbatch"] = DateTime.Now;
            newDocs.Add(code.ToObject<Document>());

            savedID = "21ad3b66-d3ec-4204-8617-c03ddb0ded31";
            respObj = handler.GetDocByID(savedID);
            code = JObject.Parse(respObj.Return);
            code["replbatch"] = DateTime.Now;
            newDocs.Add(code.ToObject<Document>());

            code["id"] = "ead28a30-7104-47c5-8691-eef70dad61f2";  //should fail
            newDocs.Add(code.ToObject<Document>());

            Task<List<WHResponse>> taskTest = handler.ReplaceBatchAsync(newDocs);
            Task.WaitAll(taskTest);
            List<WHResponse> respList = taskTest.Result;


        }
    }
}
