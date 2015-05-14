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
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace Wellhub
{
    /// <summary>
    /// Class for interacting with Azure DocumentDB.
    /// Service endpoint and authorization key for the database must be set in the configuration file of the calling program.
    /// </summary>
    public class DocHandler
    {
        const string ENDPT = "serviceEndpoint";             // Azure service endpoint for DocumentDB - used to read app.config variable
        const string AUTHKEY = "authKey";                   // Azure authorization key for DocumentDB - used to read app.config variable
        const string COLL_SELFID = "collectionSelfID";      // collection in DocumentDB - used to read app.config variable
                                                            //const string DBNAME = "database";                   // database in DocumentDB - used to read app.config variable
                                                            //const string COLLNAME = "collection";               // collection in DocumentDB - used to read app.config variable

        //root node added to json to convert to well-formed XML with namespace
        const string JSON_ROOT = "{'?xml': {'@version': '1.0','@standalone': 'no'}, 'whResponse' : { '@xmlns' : 'http://well-hub.com', 'whDocument' :";

        //variables used throughout program 
        private static string collID;                       //default collection ID if one is not passed
        private static DocumentClient client;               //document client - created once per instance for performance

        /// <summary>
        /// Returns the collection ID
        /// </summary>
        public static string CollectionID { get { return collID; } }

        private static DocumentClient Client
        {
            get
            {
                if (client == null)
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

                    try
                    {
                        // create an instance of DocumentClient from from settings in config file
                        client = new DocumentClient(new Uri(ConfigurationManager.AppSettings[ENDPT]),
                                    ConfigurationManager.AppSettings[AUTHKEY]);
                    }
                    catch (DocumentClientException docEx)
                    {
                        //if endpoint not specified throw an error 
                        if (string.IsNullOrEmpty(ConfigurationManager.AppSettings[ENDPT]))
                        {
                            throw new ApplicationException(END_PT_MSG);
                        }

                        //if authkey not specified throw an error 
                        if (string.IsNullOrEmpty(ConfigurationManager.AppSettings[AUTHKEY]))
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

        static void Main(string[] args) { }

        /// <summary>
        /// Creates an object for interacting with Azure DocumentDB.
        /// </summary>
        public DocHandler()
        {
            collID = ConfigurationManager.AppSettings[COLL_SELFID];
        }

        /// <summary>
        /// Creates an object for interacting with Azure DocumentDB.
        /// </summary>
        /// <param name="collSelfId">SelfID of the DocDB Collection to use for interaction. </param>
        public DocHandler(string collSelfId)
        {
            // if collection ID is passed, set the property
            collID = collSelfId;
        }

        /// <summary>
        /// Adds a document to the database and returns ID of new document.
        /// </summary>
        /// <param name="newDoc">The document to be created. Can be anything that evaluates to JSON: a JSON document or string, XML document or string, 
        /// a POCO (plain old CLR object), or just a string that converts to JSON</param>
        /// <returns>String containing the ID of the document that was added. </returns>
        public async Task<WHResponse> AddDocAsync(object newDoc)
        {
            //WHResponse messages
            const string DOC_NULL = "The document to be added is empty.  ";
            const string BAD_STRING = "Invalid string passed, will not serialize to JSON or XML. Raw string should be JSON or XML syntax.";
            const string BAD_COLL_ID = "Cannot open document collection with collection ID given: ";

            //other constants
            const string EMPTY_DOC = "{}";

            try
            {
                // if the document is empty, return bad request
                if (newDoc.ToString().Replace(" ", "") == EMPTY_DOC)
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
                    Document created = await Client.CreateDocumentAsync(CollectionID, newDoc);
                    return new WHResponse(WHResponse.ResponseCodes.SuccessAdd, created.Id, false, null, null, WHResponse.ContentType.Text, new Uri(created.SelfLink));
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

        /// <summary>
        /// Get single document using document ID.  Returns JSON array of documents or XML document.
        /// </summary>
        /// <param name="docID">ID of the document to return.</param>
        /// <param name="returnType">Enumerated return type, default is JSON.</param>
        /// <returns></returns>
        //public WHResponse GetDocByID(string docID, ReturnType returnType = ReturnType.JSONstring)
        //{
        //    try
        //    {
        //        //get document with matching ID and create a collection
        //        Document doc = Client.CreateDocumentQuery<Document>(CollectionID).Where(d => d.Id == docID).AsEnumerable().First();
        //        List<Document> docs = new List<Document>();
        //        docs.Add(doc);

        //        //return formatted response
        //        return FormatQueryResults(docs, returnType);
        //    }
        //    catch (Exception ex)
        //    {
        //        // return bad request
        //        return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
        //    }
        //}
        /// <summary>
        /// Get single document using document ID.  Returns JSON array of documents or XML document.
        /// </summary>
        /// <param name="docID">ID of the document to return.</param>
        /// <param name="returnType">Enumerated return type, default is JSON.</param>
        /// <returns>WHResponse object</returns>
        public WHResponse GetDocByID(string docID, ReturnType returnType = ReturnType.JSONstring)
        {
            try
            {
                //get document with matching ID 
                Expression<Func<Document, bool>> lambdaExp = d => d.Id == docID;
                IEnumerable<Document> docs = GetDocuments(lambdaExp);

                //format a response object and add selfID as link
                WHResponse resp = FormatQueryResults(docs, returnType);
                resp.Link = new Uri(docs.First().SelfLink);
                return resp;
            }
            catch (Exception ex)
            {
                // return bad request
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
            }
        }
        /// <summary>
        /// Return types for WHResponse objects
        /// </summary>
        public enum ReturnType : int
        {
            JSONstring = 1,
            XMLstring = 2
        }
        /// <summary>
        /// Formats a WHResponse object from the results of a query.
        /// </summary>
        /// <param name="docs">Collection of one or more Documents to format</param>
        /// <param name="returnType">Format of Return property.</param>
        /// <returns></returns>
        private WHResponse FormatQueryResults(IEnumerable<Document> docs, ReturnType returnType)
        {
            //if no document exists
            if (docs == null)
            {
                //return not found
                return new WHResponse(WHResponse.ResponseCodes.NotFound, null);
            }
            else
            {
                if (returnType == ReturnType.XMLstring)
                {
                    //return success as formatted XML string
                    return new WHResponse(WHResponse.ResponseCodes.SuccessGet,
                        JsonConvert.DeserializeXmlNode(string.Concat(JSON_ROOT, JsonConvert.SerializeObject(docs).ToString(), "}}")).InnerXml,
                        false, null, null, WHResponse.ContentType.XML);
                }
                else
                {
                    //return success as json
                    return new WHResponse(WHResponse.ResponseCodes.SuccessGet, JsonConvert.SerializeObject(docs),
                        false, null, null, WHResponse.ContentType.JSON);
                }
            }
        }
        /// <summary>
        /// Get documents using SQL string.  Returns JSON array of documents or XML document.
        /// </summary>
        /// <param name="sqlString">DocDB SQL string to select documents.</param>
        /// <param name="returnType">Enumerated return type, default is JSON.</param>
        /// <returns>WHResponse object</returns>
        public WHResponse GetDocs(string sqlString, ReturnType returnType = ReturnType.JSONstring)
        {
            try
            {
                //get documents using sql string in parameter and return formatted response
                return FormatQueryResults(GetDocuments(sqlString), returnType);
            }
            catch (Exception ex)
            {
                // return bad request
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
            }
        }
        /// <summary>
        /// Get documents using LINQ lambda expression.  Returns JSON array of documents or XML document.
        /// </summary>
        /// <param name="lambdaExp">LINQ lambda expression to select documents. Example: d => d.Id = "docid" where d is a Document.</param>
        /// <param name="returnType">Enumerated return type, default is JSON.</param>
        /// <returns>WHResponse object</returns>
        public WHResponse GetDocs(Expression<Func<Document,bool>> lambdaExp, ReturnType returnType = ReturnType.JSONstring)
        {
            try
            {
                //get documents using lambda exp in parameter and return formatted response
                return FormatQueryResults(GetDocuments(lambdaExp), returnType);
            }
            catch (Exception ex)
            {
                // return bad request
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
                throw;
            }
        }
        
        private IQueryable<Document> GetDocuments(object queryExp)
        {
            try
            {
                //if query is string, execute and return
                if (queryExp is string)
                {
                    string sqlString = queryExp as string;
                    return Client.CreateDocumentQuery<Document>(CollectionID, sqlString);
                }
                else
                {
                    //if query is lambda expression, execute and return
                    if (queryExp is Expression<Func<Document, bool>>)
                    {
                        Expression<Func<Document, bool>> lambdaExp = queryExp as Expression<Func<Document, bool>>;
                        return Client.CreateDocumentQuery<Document>(CollectionID).Where(lambdaExp);
                    }
                }
                // return empty set if not sql or lambda
                return null;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

            ////get documents by document type
        //public DbDocumentCollection GetDocsByType(int limit = 0, int offset = 0);

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
                IEnumerable<Document> delDocs = Client.CreateDocumentQuery(CollectionID).Where(d => d.Id == docID).AsEnumerable();

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
                    ResourceResponse<Document> retDoc = await Client.DeleteDocumentAsync(sLink);

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
    }

    public class WHResponse
    {
        /// <summary>
        /// Enumerated HTTP response codes for Wellhub Response data transfer objects.
        /// </summary>
        public enum ResponseCodes : int
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
        /// <summary>
        /// Enumerated content types for Return property of Wellhub Response data transfer objects.
        /// </summary>
        public enum ContentType : int
        {
            Text = 0,
            JSON = 1,
            XML = 2,
            HTML = 3,
            JPG = 4,
            GIF = 5,
            PNG = 6,
            PDF = 7
        }
        public ResponseCodes HTTPStatus { get; set; }
        public string Return { get; set; }
        public bool HasError { get; set; }
        public string ErrorMsg { get; set; }
        public Exception InnerException { get; set; }
        public ContentType MediaType { get; set; }
        public Uri Link { get; set; }
        public Uri AttachmentLink { get; set; }

        /// <summary>
        /// Creates a blank Wellhub Response data transfer object.
        /// </summary>
        public WHResponse() { }

        /// <summary>
        /// Creates and populates a Wellhub Response data transfer object.
        /// </summary>
        /// <param name="status">HTTP status code of the respose.</param>
        /// <param name="body">Body of the response - will be blank on errors.</param>
        /// <param name="hasErr">Indicates if processing encountered an error.</param>
        /// <param name="errMsg">Error message of processing exception.</param>
        /// <param name="innerEx">Inner exception object - wraps a processing exception.</param>
        /// <param name="contentType">Internet media type of the Return body.  Default is J</param>
        /// <param name="link">URI containing link to get associated document if there is one.</param>
        /// <param name="attLink">URI containing link to get attachment(s) of associated document if there is one.</param>
        public WHResponse(ResponseCodes status, string body, bool hasErr = false, string errMsg = null, Exception innerEx = null, 
            ContentType contentType = ContentType.Text, Uri link = null, Uri attLink = null)
        {
            //return a response object from parms
            HTTPStatus = status;
            Return = body;
            HasError = hasErr;
            ErrorMsg = errMsg;
            InnerException = innerEx;
            MediaType = contentType;
            Link = link;
            AttachmentLink = attLink;
        }

        private string Content(ContentType intContent)
        {
            switch (intContent)
            {
                case ContentType.Text:
                    return "text/plain";
                case ContentType.JSON:
                    return "application/json";
                case ContentType.XML:
                    return "application/xml";
                case ContentType.HTML:
                    return "text/html";
                case ContentType.GIF:
                    return "image/gif";
                case ContentType.JPG:
                    return "image/jpeg";
                case ContentType.PNG:
                    return "image/png";
                case ContentType.PDF:
                    return "application/pdf";
                default:
                    return "text/plain";
            }
        }
    }
}
