using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Wellhub
{
    /// <summary>
    /// Class for interacting with Azure DocumentDB.
    /// Service endpoint and authorization key for the database must be set in the configuration file of the calling program.
    /// Collection selfID must also be set for this version of the program.  
    /// </summary>
    public class DocHandler 
    {
        private static DocumentClient client;               // document client used for querying
        const string ENDPT = "serviceEndpoint";             // Azure service endpoint for DocumentDB - used to read app.config variable
        const string AUTHKEY = "authKey";                   // Azure authorization key for DocumentDB - used to read app.config variable
        const string COLL_SELFID = "collectionSelfID";      // collection in DocumentDB - used to read app.config variable
        //const string DBNAME = "database";                   // database in DocumentDB - used to read app.config variable
        //const string COLLNAME = "collection";               // collection in DocumentDB - used to read app.config variable
        
        //variable to hold collection ID
        private static string collID = ConfigurationManager.AppSettings[COLL_SELFID];

        /// <summary>
        /// Returns the collection ID
        /// </summary>
        public static string CollectionID { get { return collID; }  }

        static void Main(string[] args) { }

        /// <summary>
        /// Adds a document to the database and returns ID of new document.
        /// </summary>
        /// <param name="newDoc">The document to be created. Can be anything that evaluates to JSON: a JSON document or string, XML document or string, 
        /// a POCO (plain old CLR object), or just a string that converts to JSON</param>
        /// <returns>String containing the ID of the document that was added. </returns>
        public async Task<WHResponse> AddDocAsync(object newDoc)
        {
            //WHResponse messages
            const string DOC_NULL       = "The document to be added is empty.  ";
            const string BAD_STRING     = "Invalid string passed, will not serialize to JSON or XML. Raw string should be JSON or XML syntax.";
            const string BAD_COLL_ID    = "Cannot open document collection with collection ID given: ";

            //other constants
            const string EMPTY_DOC      = "{}";

            try
            {
                // if the document is empty, return bad request
                if (newDoc.ToString().Replace(" ","") == EMPTY_DOC)
                {
                    return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, DOC_NULL);
                }
                else
                {
                    // if the parameter passed was a string and not a formal object
                    if (newDoc is string)
                    {
                        try
                        {
                            //try converting to JSON object and reassigning
                            JObject jStr = JObject.Parse(newDoc.ToString());
                            newDoc = jStr;
                        }
                        catch (Exception jEx)
                        {
                            //if string is not XML
                            string testStr = newDoc as string;
                            if (testStr.First() != Convert.ToChar("<"))
                            {
                                // return invalid string
                                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, BAD_STRING, jEx);
                            }
                            else
                            {
                                try
                                {
                                    XmlDocument docXML = new XmlDocument();
                                    docXML.InnerXml = newDoc.ToString();
                                    newDoc = docXML;
                                }
                                catch (Exception xEx)
                                {
                                    // return invalid string
                                    return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, BAD_STRING, xEx);
                                }
                            }
                        }
                    }
                    // call create document method and return ID of created document
                    Document created = await SetDocClient().CreateDocumentAsync(CollectionID, newDoc);
                    return new WHResponse(WHResponse.ResponseCodes.SuccessAdd, created.Id);
                }
            }
            catch (ApplicationException appEx)
            {
                // return invalid client
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, appEx.Message, appEx);
            }
            catch (Exception ex)
            {
                string msg = "";
                
                //if document client is not null
                if (client != null)
                {
                    //check to see if collection id is valid
                    ResourceResponse<DocumentCollection> docCol = await client.ReadDocumentCollectionAsync(CollectionID);
                    if (docCol == null)
                    {
                        msg = string.Concat(BAD_COLL_ID, CollectionID, ". ");
                    }
                }
                // create message - invalid client - and throw exception
                throw new ApplicationException(string.Concat(msg, ex.Message), ex);
            }
        }

        /// <summary>
        /// Add a batch of documents. Returns List of document IDs in same order as submitted IDs (blank if error - check exceptions).
        /// </summary>
        /// <param name="newDocColl">IEnumerable(object) of documents. </param>
        /// <returns>List(string) of status codes (204=success, 404=not found, 500=error)</returns>
        public async Task<List<WHResponse>> AddBatchAsync(IEnumerable<object> newDocColl)
        {
            try
            {
                // create a query to get each doc object from collection submitted
                IEnumerable<Task<WHResponse>> addQuery = from docObj in newDocColl select AddDocAsync(docObj);

                // execute the query into an array of tasks 
                Task<WHResponse>[] addTasks = addQuery.ToArray();

                // load the results of each task into an array
                WHResponse[] results = await Task.WhenAll(addTasks);

                // iterate array to list for performance
                List<WHResponse> respList = new List<WHResponse>();
                for (int i = 0; i < results.Length; i++)
                {
                    respList.Add(results[i]);
                }
                //return results as a list
                return respList;
            }
            catch (Exception ex)
            {
                // return bad request
                List<WHResponse> respList = new List<WHResponse>();
                respList.Add(new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex));
                return respList;
            }
        }

        //get single document using document ID
        public WHResponse GetDocByID(string docID, ReturnType returnType = ReturnType.JSONstring)
        {
            try
            {
                Document doc = SetDocClient().CreateDocumentQuery<Document>(CollectionID).Where(d => d.Id == docID).AsEnumerable().First();

                if (doc == null)
                {
                    //return not found
                    return new WHResponse(WHResponse.ResponseCodes.NotFound, null);
                }
                else
                {
                    switch (returnType)
                    {
                        case ReturnType.JSONstring:

                             //return success as json
                            return new WHResponse(WHResponse.ResponseCodes.SuccessGet, JsonConvert.SerializeObject(doc).ToString());

                        case ReturnType.XMLstring:

                            //return success as xml
                            return new WHResponse(WHResponse.ResponseCodes.SuccessGet, JsonConvert.DeserializeXmlNode(doc.ToString()).ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                // return bad request
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
            }

        }
        public enum ReturnType : int
        {
            JSONstring = 1,
            XMLstring = 2
        }

        ////get documents by document type
        //public DbDocumentCollection GetDocsByType(int limit = 0, int offset = 0);

        ////query documents
        //public DbDocumentCollection QueryDocs(string sqlString);
        //public DbDocumentCollection QueryDocs(SqlQuerySpec sqlQuerySpec);
        //public DbDocumentCollection QueryDocs(DbDocumentCollection linqQuery);

        ////replace documents
        //public void ReplaceDoc(DbDocument newDoc);
        //public void ReplaceBatchAsync(DbDocumentCollection newDocBatch);

        ////update documents
        //public void UpdateDoc(DbDocument newDoc);
        //public void UpdateDoc(IEnumerable<JObject> docJSON);
        //public void UpdateDoc(XmlDocument docXML);
        //public void UpdateDoc(string docID, string[] propValueArray);

        ////update documents
        //public Task<List<string>> UpdateBatchAsync(DbDocumentCollection newDocBatch);
        //public Task<List<string>> UpdateBatchAsync(IEnumerable<JObject> docJSONBatch);
        //public Task<List<string>> UpdateBatchAsync(XmlDocument docXMLBatch);
        //public Task<List<string>> UpdateBatchAsync(string[] updateArray);

        ////delete documents
        /// <summary>
        /// Deletes a document from the database and returns HTTP status code of operation (204=success, 404=not found).
        /// </summary>
        /// <param name="docID">The ID of the document to be deleted. If not found, returns HTTP status code 404.</param>
        /// <returns>Integer containing HTTP status code: 204=success; 404=not found; </returns>
        public async Task<WHResponse> DeleteDocAsync(string docID)
        {
            try
            {
                // call create document method and return ID of created document
                IEnumerable<Document> delDocs = SetDocClient().CreateDocumentQuery(CollectionID).Where(d => d.Id == docID).AsEnumerable();

                //if there are no docs with that ID
                if (delDocs.Count() == 0)
                {
                    // return http status for not found
                    return new WHResponse(WHResponse.ResponseCodes.NotFound, null);
                }
                else
                {
                    //get the self link of document and delete
                    string sLink = delDocs.First().SelfLink;
                    ResourceResponse<Document> retDoc = await SetDocClient().DeleteDocumentAsync(sLink);

                    //return http status for delete success (no content)
                    return new WHResponse(WHResponse.ResponseCodes.SuccessDelete, null);
                }
            }
            catch (Exception ex)
            {
                // return invalid client
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
            }
        }
        /// <summary>
        /// Delete a batch of documents. Returns List of status codes (204=success, 404=not found, 500=error) in same order as submitted IDs.
        /// </summary>
        /// <param name="docIDs">List(string) of document IDs. If found will be deleted (204 status), if not skipped (404)</param>
        /// <returns>List(int) of status codes (204=success, 404=not found, 500=error)</returns>
        public async Task<List<WHResponse>> DeleteBatchAsync(List<string> docIDs)
        {
            try
            {
                // create a query to get each doc ID from list submitted
                IEnumerable<Task<WHResponse>> delQuery = from docID in docIDs select DeleteDocAsync(docID);

                // execute the query into an array of tasks 
                Task<WHResponse>[] deleteTasks = delQuery.ToArray();

                // load the results of each task into an array
                WHResponse[] results = await Task.WhenAll(deleteTasks);

                // iterate array to list for performance
                List<WHResponse> respList = new List<WHResponse>();
                for (int i = 0; i < results.Length; i++)
                {
                    respList.Add(results[i]);
                }

                //return results as a list
                return results.ToList();
            }
            catch (Exception ex)
            {
                // return bad request
                List<WHResponse> respList = new List<WHResponse>();
                respList.Add(new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex));
                return respList;
            }
        }

        private static DocumentClient SetDocClient()
        {
            const string DOC_ERR_MSG = "The Document Client could not be created from stored credentials.  ";
            const string END_PT_MSG = "The DocumentDB end point is not specified.  ";
            const string AUTH_KEY_MSG = "The DocumentDB authorization key is not specified.  ";
            const string AGG_ERR_MSG = " Errors Occurred. ";
            const string STAT_TEXT = ", StatusCode: ";
            const string ACT_TEXT = ", Activity id: ";
            const string ERR_TYPE = "Error type: ";
            const string MSG_TEXT = ", Message: ";
            const string BASE_MSG_TEXT = ", BaseMessage: ";

            // if there is no client already
            if (client == null)
            {
                try
                {
                    // create an instance of DocumentClient from from settings in config file
                    client = new DocumentClient(new Uri(ConfigurationManager.AppSettings[ENDPT]),
                                ConfigurationManager.AppSettings[AUTHKEY]);
                }
                catch (DocumentClientException docEx)
                {
                    //if endpoint not specified throw an error 
                    if (string.IsNullOrEmpty(ConfigurationManager.AppSettings[ENDPT]) )
                    {
                        throw new ApplicationException(END_PT_MSG);
                    }
                    
                    //if authkey not specified throw an error 
                    if (string.IsNullOrEmpty(ConfigurationManager.AppSettings[AUTHKEY]) )
                    {
                        throw new ApplicationException(AUTH_KEY_MSG);
                    }
                    
                    // get base exception 
                    Exception baseException = docEx.GetBaseException();

                    // create application exception
                    ApplicationException appEx = new ApplicationException(string.Concat(DOC_ERR_MSG, STAT_TEXT, docEx.StatusCode, 
                        ACT_TEXT, docEx.ActivityId, MSG_TEXT, docEx.Message, BASE_MSG_TEXT, baseException.Message), baseException);
                        
                    //throw the error
                    throw appEx;
                }
                catch (AggregateException aggEx)
                {
                    // create error message
                    var msg = string.Concat(aggEx.InnerExceptions.Count, AGG_ERR_MSG);

                    // for each exception
                    foreach (var ex in aggEx.InnerExceptions)
                    {
                        // try casting as document client exception
                        DocumentClientException docEx = ex as DocumentClientException;

                        // if success
                        if (docEx != null)
                        {
                            // append document client message
                            msg = string.Concat(msg, "[", DOC_ERR_MSG, STAT_TEXT, docEx.StatusCode,
                        ACT_TEXT, docEx.ActivityId, MSG_TEXT, docEx.Message, BASE_MSG_TEXT, ex.GetBaseException().Message, "]");
                        }
                        else
                        {
                            //append other message
                            msg = string.Concat(msg, "[", ERR_TYPE, ex.GetType(), MSG_TEXT, docEx.Message, "]");
                        }
                    }

                    //throw the error
                    throw new ApplicationException(msg, aggEx);
                }
                catch (Exception ex)
                {
                    //throw the error
                    throw new ApplicationException(string.Concat(ERR_TYPE, ex.GetType(), MSG_TEXT, ex.Message), ex);
                }
            }
            return client;
        }
    }

    public class WHResponse
    {
        public enum ResponseCodes :int
        {
            //response codes are same as for Microsoft DocumentDB public API
            SuccessGet = 200,           //HTTP ok
            SuccessAdd = 201,           //HTTP created
            SuccessUpdate = 200,        //HTTP ok
            SuccessDelete = 204,        //HTTP no content     
            NotFound = 404,             //HTTP not found 
            BadRequest = 400,           //HTTP bad request
            Unauthorized = 401,         //HTTP unauthorized
            Forbidden = 403             //HTTP forbidden
        }
        public ResponseCodes HTTPStatus { get; set; }
        public string Return { get; set; }
        public bool HasError { get; set; }
        public string ErrorMsg { get; set; }
        public Exception InnerException { get; set; }

        public WHResponse() { }

        public WHResponse(ResponseCodes status, string body, bool hasErr = false, string errMsg = null, Exception innerEx = null)
        {
            //return a response object from parms
            HTTPStatus = status;
            Return = body;
            HasError = hasErr;
            ErrorMsg = errMsg;
            InnerException = innerEx;
        }
    }
}
