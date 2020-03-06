using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Data;
using System.Data.Linq;
using System.Threading.Tasks;
using System.Data.Services;
using System.Data.Services.Common;
using System.Data.Services.Client;
using System.Net;
using System.Net.Security;
using System.Net.Cache;
using System.Web;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Data;
using Microsoft.Data.Edm;
using Microsoft.Data.OData;
//using Microsoft.Data.Spatial;
//NewtonSoft, Json.Net
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Framework_ServiceLayer.ServiceLayer;
using Framework_ServiceLayer.ServiceLayer.SAPB1;
//Service Reference to Service-Layer


/*
 * Owner : Business One, Refactor ToT.
 * Code to consume Service-Layer odata service in WCF client, with following functionalities supported:
 * 1. Login / Logout, both HTTP & HTTPs are supported
 * 2. GetAll / Get / GetCount / Insert / Update / Patch / Delete / Paging
 * 3. Close / Cancel. Reopen is not included for not in release scope currently.
 * 4. Left Issue: The way to ignore non-touched non-nullable properties, depends on customer-developers.
 * ==>Remember to detach the entity object from the container, if the operation fails with excpetions, else the consequent operations will be blocked and fail.
 */

//Refer to MSDN is the bese choice
//http://msdn.microsoft.com/en-us/library/cc668772(v=vs.103).aspx



namespace Framework_ServiceLayer.service
{
    //Object used by Json.Net to format json string, as content in POST/Login action.
   public class SboCred
    {
        public SboCred()
        { }

        public SboCred(string user, string pass, string company)
        {
            UserName = user;
            Password = pass;
            CompanyDB = company;
        }

        public bool IsValid()
        {
            return (!string.IsNullOrEmpty(UserName) && !string.IsNullOrEmpty(Password) && !string.IsNullOrEmpty(CompanyDB));
        }

        public string GetJsonString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented);
        }

        public string UserName = string.Empty;
        public string Password = string.Empty;
        public string CompanyDB = string.Empty;
    }


    //Currently Supported Actions
    enum DocActiontType
    {
        Close = 1,
        Cancel = 2
    }


    //The property types when formatting JSON in WCF client.
    enum PropertyType
    {
        SimpleEdmx = 0,
        ComplexType = 1,
        Collection = 2              //Collection of complex types
    }


    enum UpdateSemantics
    {
        PUT = 0,
        PATCH = 1
    }

    class PageLinks
    {
        public DataServiceQueryContinuation<Document> currentLink = null;
        public DataServiceQueryContinuation<Document> prevLink = null;
        public DataServiceQueryContinuation<Document> nextLink = null;

        public PageLinks(DataServiceQueryContinuation<Document> cLink, DataServiceQueryContinuation<Document> pLink, DataServiceQueryContinuation<Document> nLink)
        {
            currentLink = cLink;
            prevLink = pLink;
            nextLink = nLink;
        }
    }

    public class ServiceLayerService
    {
        //Singleton
        public static ServiceLayerService GetInstance()
        {
            if (null == s_Instance)
            {
                s_Instance = new ServiceLayerService();
            }

            return s_Instance;
        }

        public ServiceLayerService()
        {

        }

        private static ServiceLayerService s_Instance = null;                       //Singleton
        private string strCurrentServerURL = string.Empty;                          //Hold the service URL
        private string strCurrentSessionGUID = string.Empty;                        //Hold the session ID
        private string strCurrentRouteIDString = string.Empty;

        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        private ServiceLayer.SAPB1.ServiceLayer currentServiceContainer = null;     //EntityContainer:  DataServiceContext
        private int currentDefaultPagingSizing = 10;                          //default paging size: 10

        private StringBuilder sbHttpRequestHeaders = new StringBuilder();           //Used to build HttpHeaders
        private StringBuilder sbHttpResponseHeaders = new StringBuilder();           //Used to build HttpHeaders
        ////////////////////////////////////////////////////////////////////////////////////////////////////////
        int currentPage = -1;
        private Hashtable pageLinksList = new Hashtable();
        //private DataServiceQueryContinuation<Document> nextLink = null;         // Hold the next orders page 
        //private DataServiceQueryContinuation<Document> currentLink = null;         // Hold the current orders page 
        ////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Get Current Paging size (stored in internal variable)
        /// </summary>
        /// <returns></returns>
        public int GetCurrentPagingSize()
        {
            return currentDefaultPagingSizing;
        }

        /// <summary>
        /// Calculate current page offset based on nextLink stored
        /// </summary>
        /// <returns></returns>
        //public int GetCurrentOffset()
        //{
        //    string link = nextLink.ToString();
        //    string offset = link.Substring(link.IndexOf('=')+1);
        //    return int.Parse(offset);
        //}
            
        private PropertyType GetPropertyType(ODataProperty prop)
        {
            PropertyType retType = PropertyType.SimpleEdmx;
            if (null != prop.Value)
            {
                Type propType = prop.Value.GetType();               //Complex typed property
                switch (propType.Name)
                {
                    case "ODataComplexValue":
                        retType = PropertyType.ComplexType;
                        break;

                    case "ODataCollectionValue":
                        retType = PropertyType.Collection;
                        break;


                    default:
                        break;
                }
            }

            return retType;
        }


        /// <summary>
        /// Create new ODataComplexValue from the old, to discard all null/zero values
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private ODataComplexValue RebuildComplexValue(ODataComplexValue source)
        {
            ODataComplexValue newVal = new ODataComplexValue();
            newVal.TypeName = source.TypeName;

            List<ODataProperty> complexSons = source.Properties.ToList();

            //Filter to get new list
            List<ODataProperty> filteredSons = new List<ODataProperty>();
            foreach (ODataProperty prop in complexSons)
            {
                PropertyType retType = GetPropertyType(prop);
                switch (retType)
                {
                    case PropertyType.SimpleEdmx:
                        {
                            if (null != prop.Value)
                            {
                                if (prop.Value.GetType().Name == "Int32")
                                {
                                    //Check the value now.
                                    bool bInclude = false;
                                    try
                                    {
                                        //TODO: You cannot simply do this, potential bugs there maybe.
                                        //Use your own logics the determine if need to ignore ZEORs or not.
                                        int val = Convert.ToInt32(prop.Value);
                                        bInclude = (0 != val);
                                    }
                                    catch (Exception)
                                    {
                                    }

                                    if (bInclude)
                                        filteredSons.Add(prop);
                                }
                                else
                                    filteredSons.Add(prop);
                            }

                        }
                        break;


                    case PropertyType.ComplexType:
                        {
                            //Recursively
                            ODataComplexValue comx = RebuildComplexValue((ODataComplexValue)prop.Value);
                            if (comx.Properties.Count() > 0)
                            {
                                prop.Value = comx;
                                filteredSons.Add(prop);
                            }
                        }
                        break;


                    case PropertyType.Collection:
                        {
                            ODataCollectionValue coll = RebuildCollectionValue((ODataCollectionValue)prop.Value);
                            List<ODataComplexValue> listSubs = (List<ODataComplexValue>)coll.Items;
                            if (listSubs.Count > 0)
                            {
                                prop.Value = coll;
                                filteredSons.Add(prop);
                            }
                        }
                        break;


                    default:
                        break;
                }
            }

            //Re-Assign sons
            newVal.Properties = filteredSons;

            return newVal;
        }


        /// <summary>
        /// Create new ODataCollectionValue from the old, to discard all null/empty values
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        private ODataCollectionValue RebuildCollectionValue(ODataCollectionValue source)
        {
            ODataCollectionValue newVal = new ODataCollectionValue();
            newVal.TypeName = source.TypeName;


            List<ODataComplexValue> listComplexValues = new List<ODataComplexValue>();
            foreach (ODataComplexValue complex in source.Items)
            {
                ODataComplexValue comx = RebuildComplexValue(complex);
                listComplexValues.Add(comx);
            }

            newVal.Items = listComplexValues;

            return newVal;
        }



        /// <summary>
        /// Create new top level property set, to filter all empty collection, null/zero values if "bIgnore" is true.
        /// </summary>
        /// <param name="listSource"></param>
        /// <param name="bIgnore"></param>
        /// <returns></returns>
        private List<ODataProperty> FilterNullValues(List<ODataProperty> listSource, bool bIgnore = false)
        {
            List<ODataProperty> listResults = new List<ODataProperty>();

            if (bIgnore)
            {
                foreach (ODataProperty prop in listSource)
                {
                    PropertyType retType = GetPropertyType(prop);

                    switch (retType)
                    {
                        case PropertyType.SimpleEdmx:
                            {
                                if (null != prop.Value)
                                    listResults.Add(prop);
                            }
                            break;

                        case PropertyType.ComplexType:
                            {
                                ODataComplexValue complex = RebuildComplexValue((ODataComplexValue)prop.Value);
                                if (complex.Properties.Count() > 0)
                                {
                                    prop.Value = complex;
                                    listResults.Add(prop);
                                }
                            }
                            break;

                        case PropertyType.Collection:
                            {
                                ODataCollectionValue coll = RebuildCollectionValue((ODataCollectionValue)prop.Value);
                                List<ODataComplexValue> listSubs = (List<ODataComplexValue>)coll.Items;
                                if (listSubs.Count > 0)
                                {
                                    prop.Value = coll;
                                    listResults.Add(prop);
                                }
                            }
                            break;


                        default:
                            break;
                    }
                }
            }
            else
            {
                listResults.AddRange(listSource);                           //Original one
            }

            return listResults;
        }


        private static bool RemoteSSLTLSCertificateValidate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors ssl)
        {
            //accept
            return true;
        }


        /// <summary>
        /// Create new entity container for our business usage.
        /// </summary>
        /// <param name="strServerURL"></param>
        public void InitServiceContainer(string strServerURL)
        {
            //Cache any way, this one maybe redirected
            if (strServerURL.EndsWith("/"))
                strCurrentServerURL = strServerURL;             //Without last stroke / is preferred, usually
            else
                strCurrentServerURL = strServerURL + "/";



            if (null == currentServiceContainer)
            {
                Uri service = new Uri(strCurrentServerURL);
                currentServiceContainer = new ServiceLayer.SAPB1.ServiceLayer(service);
                if (null != currentServiceContainer)
                {
                    //Indicate the WCF to Json Format. Default is Atom.
                    currentServiceContainer.Format.UseJson();
                    currentServiceContainer.Format.UseJson();

                    //This flag : Seems not work
                    currentServiceContainer.IgnoreMissingProperties = true;

                    //Chance for us to filter propeties using our own logics.
                    currentServiceContainer.Configurations.RequestPipeline.OnEntryStarting((arg) =>
                    {
                        //For exam: Make all null properties [top level] under entity ignored
                        //arg.Entry.Properties = arg.Entry.Properties.Where((prop) => prop.Value != null);

                        if (!(arg.Entity is BusinessPartner))
                        {
                            //WCF & .NET rules:
                            //A. All value types are initialized with ZEROs, and reference types with null.
                            //B. For primitive types in WCF entities:
                            //==>non-nullable properties are raw types, nullable types are wrapped to be nullable reference types.
                            arg.Entry.Properties = FilterNullValues(arg.Entry.Properties.ToList(), true);
                        }
                    });


                    //Attach or revise the headers for carring the sesssion id, or set the paging size.
                    currentServiceContainer.SendingRequest += currentServiceContainer_SendingRequest;
                    currentServiceContainer.ReceivingResponse += currentServiceContainer_ReceivingResponse;

                    //work around WCF client cache mechanism.
                    currentServiceContainer.MergeOption = MergeOption.OverwriteChanges;

                    //SSL, TLS certificate
                    ServicePointManager.ServerCertificateValidationCallback += RemoteSSLTLSCertificateValidate;
                }
            }
        }

        /// <summary>
        /// Method being call for each response received from Service Layer
        /// Take care of B1SESSION and ROUTEID details
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void currentServiceContainer_ReceivingResponse(object sender, ReceivingResponseEventArgs e)
        {
            if (null == e.ResponseMessage)
                return;
            
            string strMessage = e.ResponseMessage.GetHeader("Set-Cookie");
            //Format of the Set-Cookie content in response of login action
            //B1SESSION=146eae44-fc3a-11e3-8000-047d7ba5aff2;HttpOnly;,ROUTEID=.node2; path=/b1s

            //Format of the cookie to be sent in request
            //Cookie: B1SESSION=57a86a60-fc3a-11e3-8000-047d7ba5aff2; ROUTEID=.node1

            if (false == string.IsNullOrEmpty(strMessage))
            {
                //The ROUTEID information will be returned during login, if sever is configured to be "Clustered" Mode.
                int idx = strMessage.IndexOf("ROUTEID=");
                if(idx > 0)
                {
                    string strSubString = strMessage.Substring(idx);
                    int idxSplitter = strSubString.IndexOf(";");
                    if(idxSplitter > 0)
                    {
                        strCurrentRouteIDString = strSubString.Substring(0, idxSplitter);
                    }
                    else
                    {
                        strCurrentRouteIDString = string.Empty;
                    }
                }
            }

            // Just for login
            BuildResponseStringContent(e.ResponseMessage);
        }


        /// <summary>
        /// Create Header
        /// </summary>
        /// <param name="strName"></param>
        /// <param name="strValue"></param>
        /// <returns></returns>
        private string CreateHeaderItem(string strName, string strValue)
        {
            string strFormat = "{0} : {1}\n";
            return string.Format(strFormat, strName, strValue);
        }


        /// <summary>
        /// Build the HTTP response headers to be string for checking usage, on GUI.
        /// </summary>
        /// <param name="response"></param>
        private void BuildResponseStringContent(IODataResponseMessage response)
        {
            sbHttpResponseHeaders.Clear();

            if (null != response)
            {
                sbHttpResponseHeaders.Append(CreateHeaderItem("StatusCode", response.StatusCode.ToString()));
                if (response.GetHeader("DataServiceVersion") != "")
                    sbHttpResponseHeaders.Append(CreateHeaderItem("DataServiceVersion", response.GetHeader("DataServiceVersion")));
                if (response.GetHeader("Date") != "")
                    sbHttpResponseHeaders.Append(CreateHeaderItem("Date", response.GetHeader("Date")));
                sbHttpResponseHeaders.Append("\n\n");
            }
        }

        /// <summary>
        /// Build the HTTP request headers to be string for checking usage, on GUI.
        /// </summary>
        /// <param name="request"></param>
        private void BuildRequestStringContent(HttpWebRequest request)
        {
            sbHttpRequestHeaders.Clear();

            if (null != request)
            {
                sbHttpRequestHeaders.Append(CreateHeaderItem("Method", request.Method.ToString()));
                //sbHttpRequestHeaders.Append(CreateHeaderItem("ServerURI", request.ServicePoint.Address.AbsoluteUri.ToString()));
                //sbHttpRequestHeaders.Append(CreateHeaderItem("Address", request.Address.ToString()));
                sbHttpRequestHeaders.Append(CreateHeaderItem("RequestUri", request.RequestUri.ToString()));
                sbHttpRequestHeaders.Append(CreateHeaderItem("Accept", request.Accept));
                sbHttpRequestHeaders.Append(CreateHeaderItem("Keep-Alive", request.KeepAlive.ToString()));
                sbHttpRequestHeaders.Append(CreateHeaderItem("ContentType", request.ContentType));
                sbHttpRequestHeaders.Append(CreateHeaderItem("ContentLength", request.ContentLength.ToString()));
                sbHttpRequestHeaders.Append(CreateHeaderItem("Connection", request.Connection));
                sbHttpRequestHeaders.Append(CreateHeaderItem("UserAgent", request.UserAgent));
                sbHttpRequestHeaders.Append(CreateHeaderItem("Timeout", request.Timeout.ToString()));                
                sbHttpRequestHeaders.Append(CreateHeaderItem("ProtocolVersion", request.ProtocolVersion.ToString()));

                sbHttpRequestHeaders.Append(CreateHeaderItem("Cookie", request.Headers["Cookie"]));
                sbHttpRequestHeaders.Append(CreateHeaderItem("Prefer", request.Headers["Prefer"]));

                sbHttpRequestHeaders.Append("\n\n");
            }
        }

        /// <summary>
        /// Get request header
        /// </summary>
        /// <returns></returns>
        public string GetRequestHeaders()
        {
            return sbHttpRequestHeaders.ToString();
        }

        /// <summary>
        /// Get response header
        /// </summary>
        /// <returns></returns>
        public string GetResponsetHeaders()
        {
            return sbHttpResponseHeaders.ToString();
        }

        /// <summary>
        /// Method called before each request is sent to Service Layer
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void currentServiceContainer_SendingRequest(object sender, System.Data.Services.Client.SendingRequestEventArgs e)
        {
            //throw new NotImplementedException();
            HttpWebRequest request = (HttpWebRequest)e.Request;
            if (null != request)
            {
                request.Accept = "application/json;odata=minimalmetadata";
                request.KeepAlive = true;                               //keep alive
                request.ServicePoint.Expect100Continue = false;        //content
                request.AllowAutoRedirect = true;
                request.ContentType = "application/json;odata=minimalmetadata;charset=utf8";
                request.Timeout = 10000000;    //number of seconds before considering a request as timeout (consider to change it for batch operations)

                //This way works to bring additional information with request headers
                if (false == string.IsNullOrEmpty(strCurrentSessionGUID))
                {
                    string strB1Session = "B1SESSION=" + strCurrentSessionGUID;
                    if (!string.IsNullOrEmpty(strCurrentRouteIDString))
                        strB1Session += "; " + strCurrentRouteIDString;

                    e.RequestHeaders.Add("Cookie", strB1Session);
                }

                //Only works for get requests, but we can always use this, even it will be ignored by other request types.
                e.RequestHeaders.Add("Prefer", "odata.maxpagesize=" + currentDefaultPagingSizing.ToString());


                //For GUI, non-functional
                BuildRequestStringContent(request);
            }
            else
                throw new Exception("Failed to intercept the sending request");
        }

        /// <summary>
        /// Empty next link value
        /// </summary>
        public void ReinitPages()
        {
            currentPage = -1;
            pageLinksList = new Hashtable();
        }

        public int GetCurrentPage()
        {
            return currentPage;
        }

        /// <summary>
        /// Login server by executing the Login action
        /// </summary>
        /// <param name="cred"></param>
        /// <returns></returns>
        public B1Session LoginServer(SboCred cred)
        {
            B1Session session = null;
            

            try
            {
                //Discard last login information
                strCurrentSessionGUID = string.Empty;

                Uri login = new Uri(strCurrentServerURL + "Login");
                //Use : UriOperationParameter for querying options.
                //Use : BodyOperationParameter for sending JSON body.
                BodyOperationParameter[] body = new BodyOperationParameter[3];
                body[0] = new BodyOperationParameter("UserName", cred.UserName);
                body[1] = new BodyOperationParameter("Password", cred.Password);
                body[2] = new BodyOperationParameter("CompanyDB", cred.CompanyDB);

                //Both HTTP & HTTPs protocols are supported.
                session = (B1Session)currentServiceContainer.Execute<B1Session>(login, "POST", true, body).SingleOrDefault();
                if (null != session)
                {
                    strCurrentSessionGUID = session.SessionId;
                }
            }
            catch (Exception ex)
            {
                strCurrentSessionGUID = string.Empty;       //clear the last time's session id
                //throw;
                throw ex;
            }

            return session;
        }



        /// <summary>
        /// Logout server by executing logout action.
        /// </summary>
        public void LogoutServer()
        {
            if (!string.IsNullOrEmpty(strCurrentSessionGUID))
            {
                try
                {
                    //Logout
                    string strRequstCmd = strCurrentServerURL + "Logout";
                    Uri cmdQuery = new Uri(strRequstCmd);
                    
                    //HTTP Request Command Type
                    string strCommandType = "POST";
                    //Enacpsulate the parameters if necessary
                    
                    UriOperationParameter[] queryOptions = null;
                    
                    currentServiceContainer.Execute(cmdQuery, strCommandType, queryOptions);

                    //Clear session id
                    strCurrentSessionGUID = string.Empty;
                }
                catch (Exception ex)
                {
                }
            }
        }

        /// <summary>
        /// Change page sizing 
        /// </summary>
        /// <param name="pgSize"></param>
        public void SetPagingSize(int pgSize)
        {
            currentDefaultPagingSizing = pgSize;
        }

        /// <summary>
        /// Send request to Service Layer to get the number of Orders   
        /// </summary>
        /// <returns></returns>
        public int GetOrdersCount()
        {
            //The currentServiceContainer.Orders won't hold all but limited by the page size.
            //But the Count returns number of the entities, and should be not limited by paging.
            return currentServiceContainer.Orders.Count();
        }


        /// <summary>
        /// Get all BusinessPartner CardCode list.
        /// </summary>
        /// <returns></returns>
        public ArrayList GetBusinessPartnerCardCodeList()
        {
            ArrayList listCardCodes = new ArrayList();

            try
            {
                //Filter data as soon as possible to reduce the amount of information to be sent through the network
                //Format the Query options
                DataServiceQuery<BusinessPartner> interRsltBPs = currentServiceContainer.BusinessPartners;
                // Only need CardCode property for our combobox
                interRsltBPs = interRsltBPs.AddQueryOption("$select", "CardCode");
                // Only need Customers to create SalesOrders
                interRsltBPs = interRsltBPs.AddQueryOption("$filter", "CardType eq 'cCustomer'");

                //MSDN : http://msdn.microsoft.com/en-us/library/dd673933(v=vs.103).aspx
                currentDefaultPagingSizing = currentServiceContainer.BusinessPartners.Count();
                List<BusinessPartner> rsltBPs = interRsltBPs.ToList();

                foreach (BusinessPartner bp in rsltBPs)
                {
                    listCardCodes.Add(bp.CardCode);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return listCardCodes;
        }

        /// <summary>
        /// Get all Items ItemCode list
        /// </summary>
        /// <returns></returns>
        public ArrayList GetItemsItemCodeList()
        {
            ArrayList listItemCodes = new ArrayList();

            try
            {
                //Filter data as soon as possible to reduce the amount of information to be sent through the network
                //Format the Query options 
                DataServiceQuery<Item> interRsltItems = currentServiceContainer.Items;
                // Only need ItemCode property to show in combobox
                interRsltItems = interRsltItems.AddQueryOption("$select", "ItemCode");

                //MSDN : http://msdn.microsoft.com/en-us/library/dd673933(v=vs.103).aspx
                currentDefaultPagingSizing = currentServiceContainer.Items.Count();
                List<Item> rsltItems = interRsltItems.ToList();

                foreach (Item itm in rsltItems)
                {
                    listItemCodes.Add(itm.ItemCode);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return listItemCodes;
        }



        /// <summary>
        /// Get all tax codes list
        /// </summary>
        /// <returns></returns>
        public ArrayList GetTaxCodeList()
        {
            ArrayList listTaxCodes = new ArrayList();

            try
            {
                currentDefaultPagingSizing = currentServiceContainer.SalesTaxCodes.Count();
                foreach (SalesTaxCode tc in currentServiceContainer.SalesTaxCodes)
                {
                    listTaxCodes.Add(tc.Code);
                }
            }
            catch (Exception ex)
            {                
                throw ex;
            }

            return listTaxCodes;
        }


        /// <summary>
        /// Retrieve previous orders page (one request to SL per page)
        /// </summary>
        /// <returns></returns>
        public ArrayList GetPrevOrdersPage()
        {
            ArrayList ordersPage = new ArrayList();
            QueryOperationResponse<Document> response = null;
 
            try
            {
                if (currentPage == 1)
                {
                    // Retrieve first Orders page
                    response = (QueryOperationResponse<Document>)currentServiceContainer.Orders.Execute();
                    //currentServiceContainer.Orders.Execute<Document>();
                }
                else
                {                 
                    response = currentServiceContainer.Execute<Document>(((PageLinks)pageLinksList[currentPage-1]).currentLink);
                }

                currentPage--;

                foreach (Document order in response)
                {
                    ordersPage.Add(order);        //Cache, copy
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return ordersPage;
        }

        /// <summary>
        /// Retrieve next orders page (one request to SL per page)
        /// </summary>
        /// <returns></returns>
        public ArrayList GetNextOrdersPage()
        {
            ArrayList ordersPage = new ArrayList();
            QueryOperationResponse<Document> response = null;
            PageLinks prevPageLinks = null;

            try
            {
                if (currentPage == -1)
                {
                    // Retrieve first Orders page
                    prevPageLinks = null;
                    response = (QueryOperationResponse<Document>)currentServiceContainer.Orders.Execute();
                }
                else
                {                    
                    prevPageLinks = (PageLinks)pageLinksList[currentPage];
                    response = currentServiceContainer.Execute<Document>(prevPageLinks.nextLink);

                }
                currentPage++;

                foreach (Document order in response)
                {
                    ordersPage.Add(order);        //Cache, copy
                }

                // Save current page information
                // Only available after going over the response
                // Register next page available if not already saved
                if (!pageLinksList.ContainsKey(currentPage))
                    pageLinksList.Add(currentPage, new PageLinks((prevPageLinks != null)?prevPageLinks.nextLink:null, (prevPageLinks != null)?prevPageLinks.currentLink:null, response.GetContinuation()));
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return ordersPage;
        }


        /// <summary>
        /// Get specific entity/order using the Key.
        /// </summary>
        /// <param name="docEntry"></param>
        /// <returns></returns>
        public Document GetOrderWithDocEntry(int docEntry)
        {
            Document order = null;

            try
            {
                order = currentServiceContainer.Orders.Where(cursor => cursor.DocEntry == docEntry).SingleOrDefault();
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return order;
        }

        /// <summary>
        /// Add a new order document into the Batch operations, all operations added will be sent to Service Layer on SaveChanges method call
        /// </summary>
        /// <param name="order"></param>
        public void AppendNewOrderToBatch(Document order)
        {
            try
            {
                currentServiceContainer.AddToOrders(order);
            }
            catch (Exception ex)
            {
                //Discard the order from beting tracking
                currentServiceContainer.Detach(order);
                throw ex;
            }
        }

        /// <summary>
        /// Send all operations added to the currentServiceContainer to the ServiceLayer
        /// </summary>
        /// <returns></returns>
        public Document SaveChangesBatch()
        {
            try
            {                
                DataServiceResponse response = currentServiceContainer.SaveChanges(SaveChangesOptions.Batch);
                if (null != response)
                {
                    //ChangeOperationResponse opRes = (ChangeOperationResponse)response.SingleOrDefault();
                    //object retDoc = ((System.Data.Services.Client.EntityDescriptor)(opRes.Descriptor)).Entity;
                    //if (null != retDoc)
                    //{
                    //    newOrderDoc = (Document)retDoc;
                    //}

                    //HTTP_CREATED, value 201, used to indicate success                    
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                //throw ex;
            }

            return null;
        }

        
        /// <summary>
        /// Add a new order via Service Layer
        /// </summary>
        /// <param name="order"></param>
        /// <returns></returns>
        public Document AddNewOrder(Document order)
        {
            Document newOrderDoc = null;

            try
            {
                currentServiceContainer.AddToOrders(order);
                DataServiceResponse response = currentServiceContainer.SaveChanges();                               
                if (null != response)
                {
                    ChangeOperationResponse opRes = (ChangeOperationResponse)response.SingleOrDefault();
                    object retDoc = ((System.Data.Services.Client.EntityDescriptor)(opRes.Descriptor)).Entity;
                    if (null != retDoc)
                    {
                        newOrderDoc = (Document)retDoc;
                    }

                    //HTTP_CREATED, value 201, used to indicate success                    
                }
            }
            catch (Exception ex)
            {
                //Discard the order from beting tracking
                currentServiceContainer.Detach(order);
                throw ex;
            }

            return newOrderDoc;
        }


        /// <summary>
        /// Update or Patch the order. The way to apply chagnes determines the semantics.
        /// </summary>
        /// <param name="docEntry"></param>
        /// <param name="semantics"></param>
        /// <param name="bUpdateComments"></param>
        /// <param name="strDesc"></param>
        /// <param name="newLine"></param>
        //public void UpdateOrder(int docEntry, UpdateSemantics semantics, bool bUpdateComments, string strDesc, DocumentLine newLine)
        //{
        //    Document docUpdate = null;

        //    try
        //    {
        //        //Try to get the order and cache it in the context
        //        docUpdate = (Document)currentServiceContainer.Orders.Where(cursor => cursor.DocEntry == docEntry).SingleOrDefault();
        //        if (null == docUpdate)
        //            throw new Exception("No order with DocEntry=" + docEntry.ToString() + " can be found");

        //        //Make changes to value of current order, which is under tracking of this document
        //        if (bUpdateComments)
        //        {
        //            docUpdate.Comments = strDesc;
        //        }
        //        else
        //        {
        //            //Try to revise the first document line
        //            newLine.DocEntry = docUpdate.DocEntry;
        //            docUpdate.DocumentLines.Add(newLine);
        //        }

        //        //Update the document now
        //        currentServiceContainer.UpdateObject(docUpdate);
        //        SaveChangesOptions updateSemantics = SaveChangesOptions.ReplaceOnUpdate;
        //        if (semantics == UpdateSemantics.PATCH)
        //            updateSemantics = SaveChangesOptions.PatchOnUpdate;

        //        currentServiceContainer.SaveChanges(updateSemantics);
        //    }
        //    catch (Exception ex)
        //    {
        //        //Discard the order from beting tracking
        //        if (null != docUpdate)
        //            currentServiceContainer.Detach(docUpdate);

        //        string strAction = (semantics == UpdateSemantics.PATCH) ? "Patch" : "Update";
        //        throw ex;
        //    }
        //}


       
        public void DeleteOrder(int docEntry)
        {
            Document docDel = null;
            try
            {
                docDel = currentServiceContainer.Orders.Where(cursor => cursor.DocEntry == docEntry).SingleOrDefault();
                if (null != docDel)
                {
                    currentServiceContainer.DeleteObject(docDel);
                    currentServiceContainer.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                //Discard the order from beting tracking
                if (null != docDel)
                    currentServiceContainer.Detach(docDel);
                throw ex;
            }
        }


        /// <summary>
        /// Call Close or Cancel commands via Execute command
        /// Generation of URI string in order to define which command to call (instead of WCF generated methods)
        /// </summary>
        /// <param name="action"></param>
        /// <param name="docEntry"></param>
        private void ExecuteCommand(DocActiontType action, int docEntry = -1)
        {
            try
            {
                //Format the URI
                string strFormat = "{0}/{1}({2})/{3}";
                string strURL = strCurrentServerURL;
                if (strCurrentServerURL.EndsWith("/"))
                    strURL = strURL.Remove(strURL.Length - 1);        //remove last /, to avoid duplication

                string strRequstCmd = string.Empty;

                //HTTP Request Command Type
                string strCommandType = "POST";

                string strAction = string.Empty;
                switch (action)
                {
                    case DocActiontType.Close:
                        {
                            strAction = "Close";                            
                        }
                        break;

                    case DocActiontType.Cancel:
                        {
                            strAction = "Cancel";
                        }
                        break;

                    default:
                        break;
                }

                strRequstCmd = string.Format(strFormat, strURL, "Orders", docEntry.ToString(), strAction);
                Uri cmdQuery = new Uri(strRequstCmd);

                //Enacpsulate the parameters if necessary
                UriOperationParameter[] queryOptions = null;
                currentServiceContainer.Execute(cmdQuery, strCommandType, queryOptions);
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Close an Order
        /// </summary>
        /// <param name="docEntry"></param>
        public void CloseOrder(int docEntry)
        {
            ExecuteCommand(DocActiontType.Close, docEntry);
        }

        /// <summary>
        /// Cancel an Order
        /// </summary>
        /// <param name="docEntry"></param>
        public void CancelOrder(int docEntry)
        {
            ExecuteCommand(DocActiontType.Cancel, docEntry);
        }


        /// <summary>
        /// Send several SELECT queries in a Batch request
        /// </summary>
        /// <param name="batchRequests"></param>
        public void BatchRequest(DataServiceRequest[] batchRequests)
        {
            try
            {
                DataServiceResponse batchResponse = null;
                currentServiceContainer.MergeOption = MergeOption.AppendOnly;

                //Batch for querying data from server, to use:  ExecuteBatch
                batchResponse = currentServiceContainer.ExecuteBatch(batchRequests);

                //Use the same way to get results
                foreach (QueryOperationResponse res in batchResponse)
                {
                    //Attention, for each QueryOperationResponse in the batchResponse, it only fall into one kind of collection.
                    //So each time, only one kind of type-cast will succeeded.

                    //Order
                    try
                    {
                        foreach (Document order in res.Cast<Document>())
                        {
                            // Do the action you want to show Document details
                        }
                    }
                    catch (Exception)
                    {
                        //throw;
                    }

                    //BusinessPartner
                    try
                    {
                        foreach (BusinessPartner bp in res.Cast<BusinessPartner>())
                        {
                            // Do the action you want to show BusinessPartner details
                        }
                    }
                    catch (Exception)
                    {
                        //throw;
                    }

                    //Item
                    try
                    {
                        foreach (Item it in res.Cast<Item>())
                        {
                            // Do the action you want to show Item details
                        }
                    }
                    catch (Exception)
                    {
                        //throw;
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// Return the list of orders based on the uriParams specified
        /// Select, filter, top, skip, orderby, inlinecount. format will be ignored by B1S currently.
        /// Use array of UriOperationParameter as input, to format the query options automatically
        /// </summary>
        /// <param name="uriParams"></param>
        /// <returns></returns>
        public ArrayList GetOrdersViaQueryOptionsAndFilters(UriOperationParameter[] uriParams)
        {
            ArrayList listOrders = new ArrayList();     //list of Documents

            try
            {
                //Format the Query options 
                DataServiceQuery<Document> interRsltDocs = currentServiceContainer.Orders;
                int idx = 0;
                foreach (UriOperationParameter uriOps in uriParams)
                {
                    UriOperationParameter para = (UriOperationParameter)(uriParams[idx]);
                    interRsltDocs = interRsltDocs.AddQueryOption(para.Name, para.Value);
                    ++idx;
                }

                //MSDN : http://msdn.microsoft.com/en-us/library/dd673933(v=vs.103).aspx
                List<Document> rsltOrders = interRsltDocs.ToList();
                
                foreach (Document doc in rsltOrders)
                {
                    listOrders.Add(doc);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            return listOrders;
        }

        /// <summary>
        /// Add a UDT
        /// </summary>
        /// <param name="userTable"></param>
        /// <returns></returns>
        public UserTablesMD AddUDT(UserTablesMD userTable)
        {           
            UserTablesMD newTable = null;
            try
            {
                currentServiceContainer.AddToUserTablesMD(userTable);
                DataServiceResponse response = currentServiceContainer.SaveChanges();
                if (null != response)
                {
                    ChangeOperationResponse opRes = (ChangeOperationResponse)response.SingleOrDefault();
                    object retTable = ((System.Data.Services.Client.EntityDescriptor)(opRes.Descriptor)).Entity;
                    if (null != retTable)
                    {
                        newTable = (UserTablesMD)retTable;
                    }

                    //HTTP_CREATED, value 201, used to indicate success                    
                }
            }
            catch (Exception ex)
            {
                //Discard the order from beting tracking
                currentServiceContainer.Detach(userTable);
                //throw ex;
            }

            return newTable;
        }

        /// <summary>
        /// Delete a UDT
        /// </summary>
        /// <param name="tableName"></param>
        public void DeleteUDT(string tableName)
        {
            UserFieldMD tableDel = null;
            try
            {
                tableDel = currentServiceContainer.UserFieldsMD.Where(cursor => cursor.TableName == tableName).SingleOrDefault();
                if (null != tableDel)
                {
                    currentServiceContainer.DeleteObject(tableDel);
                    currentServiceContainer.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                //Discard the order from beting tracking
                if (null != tableDel)
                    currentServiceContainer.Detach(tableDel);
                throw ex;
            }
        }

        /// <summary>
        /// Add a UDF
        /// </summary>
        /// <param name="userField"></param>
        /// <returns></returns>
        public UserFieldMD AddUDF(UserFieldMD userField)
        {
            UserFieldMD newField = null;

            try
            {
                currentServiceContainer.AddToUserFieldsMD(userField);
                DataServiceResponse response = currentServiceContainer.SaveChanges();
                if (null != response)
                {
                    ChangeOperationResponse opRes = (ChangeOperationResponse)response.SingleOrDefault();
                    object retField = ((System.Data.Services.Client.EntityDescriptor)(opRes.Descriptor)).Entity;
                    if (null != retField)
                    {
                        newField = (UserFieldMD)retField;
                    }

                    //HTTP_CREATED, value 201, used to indicate success                    
                }
            }
            catch (Exception ex)
            {
                //Discard the order from beting tracking
                currentServiceContainer.Detach(userField);
                //throw ex;
            }

            return newField;
        }

        /// <summary>
        /// Get a BP
        /// </summary>
        /// <param name="cardCode"></param>
        /// <returns></returns>
        public BusinessPartner GetBP(string cardCode)
        {
            BusinessPartner bp = currentServiceContainer.BusinessPartners.Where(cursor => cursor.CardCode == cardCode).SingleOrDefault();
            return bp;
        }

        /// <summary>
        /// Add a BP
        /// </summary>
        /// <param name="bp"></param>
        /// <returns></returns>
        public BusinessPartner AddBP(BusinessPartner bp)
        {
            BusinessPartner newBP = null;

            try
            {
                currentServiceContainer.AddToBusinessPartners(bp);
                DataServiceResponse response = currentServiceContainer.SaveChanges();
                if (null != response)
                {
                    ChangeOperationResponse opRes = (ChangeOperationResponse)response.SingleOrDefault();
                    object retObj = ((System.Data.Services.Client.EntityDescriptor)(opRes.Descriptor)).Entity;
                    if (null != retObj)
                    {
                        newBP = (BusinessPartner)retObj;
                    }

                    //HTTP_CREATED, value 201, used to indicate success                    
                }
            }
            catch (Exception ex)
            {
                //Discard the order from beting tracking
                currentServiceContainer.Detach(bp);
                throw ex;
            }

            return newBP;
        }

        /// <summary>
        /// Update a BP
        /// </summary>
        /// <param name="bpUpdate"></param>
        public void UpdateBP(BusinessPartner bpUpdate)
        {
             try
            {
                //Update the document now
                currentServiceContainer.UpdateObject(bpUpdate);
                SaveChangesOptions updateSemantics = SaveChangesOptions.ReplaceOnUpdate;
                updateSemantics = SaveChangesOptions.PatchOnUpdate;

                currentServiceContainer.SaveChanges(updateSemantics);
            }
            catch (Exception ex)
            {
                //Discard the bp from beting tracking
                if (null != bpUpdate)
                    currentServiceContainer.Detach(bpUpdate);

                string strAction = "Patch";
                throw ex;
            }
        }

    }
}


