using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using System.Xml;

namespace Wellhub
{
    /// <summary>
    /// Class for interacting with Azure DocumentDB. Service endpoint and authorization key for the database must be set in the Azure app.
    /// Collection can be passed as parameter or the class will use the default from the configuration file.  
    /// </summary>
    public class DocHandler
    {
        #region Constants and Variables

        const string ENDPT = "serviceEndpoint";             // Azure service endpoint for DocumentDB - used to read app.config variable
        const string AUTHKEY = "authKey";                   // Azure authorization key for DocumentDB - used to read app.config variable
        const string COLL_SELFID = "collectionSelfID";      // collection in DocumentDB - used to read app.config variable

        //root node added to json to convert to well-formed XML with namespace
        const string JSON_ROOT = "{'?xml': {'@version': '1.0','@standalone': 'no'}, 'whResponse' : { '@xmlns' : 'http://well-hub.com', 'whDocument' :";

        //WHResponse messages
        const string BAD_QUERY = "The query could not be executed as written. ";
        const string DOC_NULL = "The document to be added is empty.  ";
        const string BAD_STRING = "Invalid string passed, will not serialize to JSON or XML. Raw string should be JSON or XML syntax.";
        const string BAD_COLL_ID = "Cannot open document collection with collection ID given: ";
        const string EMPTY_ID = "The request did not specify a document ID. ";
        const string DOC_ERR_MSG = "The Document Client could not be created from stored credentials.  ";
        const string END_PT_MSG = "The DocumentDB end point is not specified.  ";
        const string AUTH_KEY_MSG = "The DocumentDB authorization key is not specified.  ";
        const string AGG_ERR_MSG = " Errors Occurred. ";
        const string STAT_TEXT = ", StatusCode: ";
        const string ACT_TEXT = ", Activity id: ";
        const string ERR_TYPE = "Error type: ";
        const string MSG_TEXT = ", Message: ";
        const string BASE_MSG_TEXT = ", BaseMessage: ";
        const string CONFLICT_MSG = "There is already a document in the database with this ID but a different SelfID.  ";
        const string NOTFOUND_MSG = "There is no document with the specified SelfID.  ";
        const string NO_SELF_ID = "The SelfID cannot be blank unless the replacement object is a Document with a SelfID set in the SelfID property.  ";

        //Other constants
        const string CONFLICT_TEXT = "conflict";
        const string NOTFOUND_TEXT = "notfound";
        const string EMPTY_DOC = "{}";

        //variables used throughout program 
        private static string collID;                       //default collection ID if one is not passed
        private static DocumentClient client;               //document client - created once per instance for performance

        #endregion

        #region Constructor and Class Properties

        /// <summary>
        /// Returns the collection ID
        /// </summary>
        public static string CollectionID { get { return collID; } }

        private static DocumentClient Client
        {
            get
            {
                //if there is no client yet
                if (client == null)
                {
                    try
                    {
                        // create an instance of DocumentClient from from settings in config file
                        client = new DocumentClient(new Uri(ConfigurationManager.AppSettings[ENDPT]),
                                    ConfigurationManager.AppSettings[AUTHKEY],
                                    new ConnectionPolicy
                                    {
                                        ConnectionMode = ConnectionMode.Direct,
                                        ConnectionProtocol = Protocol.Tcp
                                    });
                        //explicitly open for performance improvement
                        client.OpenAsync();
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
        #endregion

        #region Enumerations
        /// <summary>
        /// Operations type for DocOpsAsync and RunBatchAsync
        /// </summary>
        private enum OpsType : int
        {
            AddDoc = 0,
            UpdateDoc = 1,
            ReplaceDoc = 2,
            DeleteDoc = 3
        }

        /// <summary>
        /// Return types for WHResponse objects
        /// </summary>
        public enum ReturnType : int
        {
            JSONstring = 1,
            XMLstring = 2
        }
        #endregion

        #region Public Methods

        #region Add Methods

        /// <summary>
        /// Adds a document to the database and returns ID of new document.
        /// </summary>
        /// <param name="newDoc">The document to be created. Can be anything that evaluates to JSON: a JSON document or string, XML document or string, 
        /// a POCO (plain old CLR object), or just a string that converts to JSON</param>
        /// <returns>String containing the ID of the document that was added. </returns>
        public async Task<WHResponse> AddDocAsync(object newDoc)
        {
            return await DocOpsAsync(newDoc, OpsType.AddDoc);
        }

        /// <summary>
        /// Add a batch of documents. Returns List of document IDs in same order as submitted IDs (blank if error - check exceptions).
        /// </summary>
        /// <param name="newDocColl">IEnumerable(object) of documents. </param>
        /// <returns>List(string) of status codes (204=success, 404=not found, 500=error)</returns>
        public async Task<List<WHResponse>> AddBatchAsync(IEnumerable<object> newDocColl)
        {
            // return from the batch runner
            return await RunBatchAsync(newDocColl, OpsType.AddDoc);
        }
        #endregion

        #region Get Methods

        /// <summary>
        /// Get documents using SQL string.  Returns JSON array of documents or XML document.
        /// </summary>
        /// <param name="queryExp">DocDB SQL string or lambda expression to select documents. Sample lambda declaration: Expression(Func(Document, bool)) lambdaExp = d => d.Id == docID </param>
        /// <param name="returnType">Enumerated return type, default is JSON.</param>
        /// <param name="maxCount">Maximum number of documents to return.</param>
        /// <param name="sessionToken">Session token for consistency if required.</param>
        /// <param name="contToken">Continuation token for paging.  Taken from previous WHResponse.Continuation property.</param>
        /// <returns>WHResponse object</returns>
        public async Task<WHResponse> GetDocsAsync(object queryExp, ReturnType returnType = ReturnType.JSONstring, int maxCount = 100, string sessionToken = null, string contToken = null)
        {
            //set the feed options
            FeedOptions feedOpt = new FeedOptions
            {
                MaxItemCount = maxCount,
                SessionToken = sessionToken,
                RequestContinuation = contToken
            };
            try
            {
                // set up to receive queryable document collection based upon query expression
                IDocumentQuery<Document> queryable = GetDocuments(queryExp, feedOpt) as IDocumentQuery<Document>;

                //execute query and get results as a feed
                FeedResponse<Document> feedResp = await queryable.ExecuteNextAsync<Document>();

                // convert to response format
                WHResponse resObj = FormatQueryResults(feedResp, returnType);
                
                // store continuation token and return response object
                resObj.Continuation = feedResp.ResponseContinuation;
                return resObj;
            }
            catch (Exception ex)
            {
                // return bad request
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, string.Concat(BAD_QUERY, ex.Message), ex);
            }
        }
        
        /// <summary>
        /// Get single document using document ID.  Returns JSON array of documents or XML document in Return property.  SelfID is in the Link property.
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

                //return formatted response object
                return FormatQueryResults(docs, returnType);
            }
            catch (Exception ex)
            {
                // return bad request
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
            }
        }

        ////get documents by document type
        //public DbDocumentCollection GetDocsByType(int limit = 0, int offset = 0);
        #endregion

        #region Replace Methods

        /// <summary>
        /// Replace a document in the database with a new document
        /// </summary>
        /// <param name="newDoc">The document to be created. Can be anything that evaluates to JSON: a JSON document or string, XML document or string, 
        /// a POCO (plain old CLR object), or just a string that converts to JSON.</param>
        /// <param name="selfID">The selfID of the document to be replaced. Can be blank if newDoc parm contains a Document object with a valid SelfID property.</param> 
        /// <returns></returns>
        public async Task<WHResponse> ReplaceDocAsync(object newDoc, string selfID = null)
        {
            // if selfID is blank and newDoc is a Document, try to set the selfID from newDoc
            if (selfID == null)
                if (newDoc is Document)
                {
                    Document chkDoc = newDoc as Document;
                    selfID = chkDoc.SelfLink;
                }
                else 
                {
                    // return bad request
                    return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, NO_SELF_ID);
                }
            try
            {
                //replace document
                return await DocOpsAsync(newDoc, OpsType.ReplaceDoc, selfID);
            }
            catch (Exception ex)
            {
                // return bad request
                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, ex.Message, ex);
            }
        }

        /// <summary>
        /// Replace a batch of documents. Returns a List of WHReponse objects.
        /// </summary>
        /// <param name="newDocColl">IEnumerable(object) of Documents objects that have SelfID set in each. If unknown, getDocByID returns SelfID in the Link property. </param>
        /// <returns>List of WHResponse objects with results in each.</returns>
        public async Task<List<WHResponse>> ReplaceBatchAsync(IEnumerable<Document> newDocColl)
        {
            // return from the batch runner
            return await RunBatchAsync(newDocColl, OpsType.ReplaceDoc);
        }
        #endregion

        #region Update Methods

        //public void UpdateDoc(DbDocument newDoc);

        //public Task<List<string>> UpdateBatchAsync(DbDocumentCollection newDocBatch);
        #endregion

        #region Delete Methods

        /// <summary>
        /// Deletes a document from the database and returns HTTP status code of operation (204=success, 404=not found).
        /// </summary>
        /// <param name="docID">The ID of the document to be deleted. If not found, returns HTTP status code 404.</param>
        /// <returns>Integer containing HTTP status code: 204=success; 404=not found; </returns>
        public async Task<WHResponse> DeleteDocAsync(string docID = "")
        {

            try
            {
                //if there is a document ID
                if (docID != "") 
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
                else
                {
                    // return invalid client
                    return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, EMPTY_ID);
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
            // return from the batch runner
            return await RunBatchAsync(docIDs, OpsType.DeleteDoc);
        }
        #endregion
        #endregion

        #region Private Internal Use Methods

        /// <summary>
        /// Perform document operation (add, update or replace) on the database.
        /// </summary>
        /// <param name="newDoc">The document to be created. Can be anything that evaluates to JSON: a JSON document or string, XML document or string, 
        /// a POCO (plain old CLR object), or just a string that converts to JSON</param>
        /// <param name="operation">The enumerated operation to perform (add, update, replace).</param>
        /// <param name="selfID">The selfID of the document to replace (replace operation only).</param>
        /// <returns>String containing the ID of the document that was added. </returns>
        private async Task<WHResponse> DocOpsAsync(object newDoc, OpsType operation, string selfID = null)
        {
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
                    try
                    {
                        switch (operation)
                        {
                            case OpsType.AddDoc:

                                // call create document method and return ID of created document
                                Document created = await Client.CreateDocumentAsync(CollectionID, newDoc);
                                return new WHResponse(WHResponse.ResponseCodes.SuccessAdd, created.Id, false, null, null, WHResponse.ContentType.Text, created.SelfLink);
                        
                            //case OpsType.UpdateDoc:
                            //    break;

                            case OpsType.ReplaceDoc:

                                // call create document method and return ID of created document
                                created = await Client.ReplaceDocumentAsync(selfID, newDoc);
                                return new WHResponse(WHResponse.ResponseCodes.SuccessGetOrUpdate, created.Id, false, null, null, WHResponse.ContentType.Text, created.SelfLink);

                            default:
                                return new WHResponse(WHResponse.ResponseCodes.BadRequest, null, true, BAD_STRING);
                        }
                    }
                    catch (DocumentClientException docEx)
                    {
                        // if there is a conflict, return error message
                        if (docEx.Error.Code.ToLower() == CONFLICT_TEXT)
                        {
                            return new WHResponse(WHResponse.ResponseCodes.Conflict, newDoc.ToString(), true, string.Concat(CONFLICT_MSG, docEx.Message), docEx, WHResponse.ContentType.Text);
                        }
                        // if document not found, return error message
                        if (docEx.Error.Code.ToLower() == NOTFOUND_TEXT)
                        {
                            return new WHResponse(WHResponse.ResponseCodes.NotFound, newDoc.ToString(), true, string.Concat(NOTFOUND_MSG, docEx.Message), docEx, WHResponse.ContentType.Text);
                        }
                        //throw any other exceptions
                        throw;
                    }
                    catch (Exception)
                    {
                        //throw any other exceptions
                        throw;
                    }
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
        /// Formats a WHResponse object from the results of a query.
        /// </summary>
        /// <param name="docs">Collection of one or more Documents to format</param>
        /// <param name="returnType">Format of Return property.</param>
        /// <param name="responseCode">Response Code to put in HTTPStatus property.</param>
        /// <returns></returns>
        private WHResponse FormatQueryResults(IEnumerable<Document> docs, ReturnType returnType, WHResponse.ResponseCodes responseCode = WHResponse.ResponseCodes.SuccessGetOrUpdate)
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
                    return new WHResponse(responseCode,
                        JsonConvert.DeserializeXmlNode(string.Concat(JSON_ROOT, JsonConvert.SerializeObject(docs).ToString(), "}}")).InnerXml,
                        false, null, null, WHResponse.ContentType.XML, docs.First().SelfLink, null, null, docs.Count());
                }
                else
                {
                    //if only one document, handle json serialization separately
                    if (docs.Count() == 1)
                    {
                        //return success as json (only serialize first node to avoid sending an array with one element - causes issues in later json handling)
                        return new WHResponse(responseCode, JsonConvert.SerializeObject(docs.First()),
                            false, null, null, WHResponse.ContentType.JSON, docs.First().SelfLink, null, null, docs.Count());
                    }
                    else
                    {
                        //return success as json
                        return new WHResponse(responseCode, JsonConvert.SerializeObject(docs),
                            false, null, null, WHResponse.ContentType.JSON, docs.First().SelfLink, null, null, docs.Count());
                    }
                }
            }
        }

        private IQueryable<Document> GetDocuments(object queryExp, FeedOptions feedOpt = null)
        {
            try
            {
                //if query is string, execute and return
                if (queryExp is string)
                {
                    string sqlString = queryExp as string;
                    return Client.CreateDocumentQuery<Document>(CollectionID, sqlString, feedOpt);
                }
                else
                {
                    //if query is lambda expression, execute and return
                    if (queryExp is Expression<Func<Document, bool>>)
                    {
                        Expression<Func<Document, bool>> lambdaExp = queryExp as Expression<Func<Document, bool>>;
                        return Client.CreateDocumentQuery<Document>(CollectionID, feedOpt).Where(lambdaExp);
                    }
                }
                // return empty set if not sql or lambda
                return null;
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// Process batch for CRUD operations.  All batches are run the same way.
        /// </summary>
        /// <param name="batch">Object submitted to perform the operation.  Differs for each operation, see non-batch methods for specific type required. </param>
        /// <param name="operation">Enumerated type of operation to run.</param>
        /// <returns></returns>
        private async Task<List<WHResponse>> RunBatchAsync(IEnumerable<object> batch, OpsType operation)
        {
            try
            {
                //initialize query object
                IEnumerable<Task<WHResponse>> iQuery = null;
                
                // create a query to get each doc object from collection submitted (cannot use switch due to select statement)
                if (operation == OpsType.AddDoc)
                {
                    iQuery = from docObj in batch select AddDocAsync(docObj); 
                }
                else
                {
                    if (operation == OpsType.DeleteDoc)
                    {
                        iQuery = from docID in (batch as List<string>) select DeleteDocAsync(docID);
                    }
                    else
                    {
                        if (operation == OpsType.ReplaceDoc)
                        {
                            iQuery = from docObj in batch select ReplaceDocAsync(docObj);
                        }
                        else        //(operation == OpsType.UpdateDoc)
                        {
                            //iQuery = from docObj in batch select UpdateDocAsync(docObj); 
                        }
                    }
                }
                
                // execute the query into an array of tasks 
                Task<WHResponse>[] iTasks = iQuery.ToArray();

                // load the results of each task into an array
                WHResponse[] results = await Task.WhenAll(iTasks);

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
        #endregion
    }

    public class WHResponse
    {
        #region Constructor and Class Properties
        public ResponseCodes HTTPStatus { get; set; }
        public string Return { get; set; }
        public bool HasError { get; set; }
        public string ErrorMsg { get; set; }
        public Exception InnerException { get; set; }
        public ContentType MediaType { get; set; }
        public string Link { get; set; }
        public string AttachmentLink { get; set; }
        public string Continuation { get; set; }
        public int Count { get; set; }

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
        /// <param name="contentType">Internet media type of the Return body.  Default is JSON.</param>
        /// <param name="link">String containing internal link to get associated document if there is one.</param>
        /// <param name="attLink">String containing internal link to get attachment(s) of associated document if there is one.</param>
        /// <param name="respCont">String containing response continuation key for paging operations (used to get next page).</param>
        /// <param name="docCount">Integer showing how many documents are in Return.</param>
        public WHResponse(ResponseCodes status, string body, bool hasErr = false, string errMsg = null, Exception innerEx = null, 
            ContentType contentType = ContentType.Text, string link = null, string attLink = null, string respCont = null, int docCount = 0)
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
            Continuation = respCont;
            Count = docCount;
        }
        #endregion

        #region Enumerations
        /// <summary>
        /// Enumerated HTTP response codes for Wellhub Response data transfer objects.
        /// </summary>
        public enum ResponseCodes : int
        {
            //response codes are same as for Microsoft DocumentDB public API
            SuccessGetOrUpdate = 200,   //HTTP ok
            SuccessAdd = 201,           //HTTP created
            SuccessDelete = 204,        //HTTP no content     
            BadRequest = 400,           //HTTP bad request
            Unauthorized = 401,         //HTTP unauthorized
            Forbidden = 403,            //HTTP forbidden
            NotFound = 404,             //HTTP not found 
            Conflict = 409,             //HTTP conflict
            TooLarge = 413              //HTTP entity too large
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
        #endregion

        #region Private Internal Use Methods
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
        #endregion
    }
}
