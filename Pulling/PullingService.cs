using log4net;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using System.Net.Http;
using System.Net;
using System.Runtime.Serialization.Json;
using System.IO;
using System.Text;

using System.Linq;
using System.ServiceModel.Channels;
using System.ServiceModel;

namespace Pulling
{
    public class PullingService
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private int sleepTime;
        
        
        public PullingService()
        {
            sleepTime = Convert.ToInt32(ConfigurationManager.AppSettings["SleepTime"]);
        }

        public   void Run()
        {
            try
            {
                log.Info("=============================================================================");
                log.Info("=============================================================================");

                log.Info("start service");

                var taskOrder = new Task(() =>
                {
                    while (true)
                    {
                        ChamaServico();
                        Thread.Sleep(new TimeSpan(1, 6, 0, 0, 0));
                        //(sleepTime, 0, 0));
                    }
                });
                taskOrder.Start();
            }
            catch (Exception ex)
            {

                log.Fatal("Fatal Error order message = " + ex.Message);
            }
        }
        public void ChamaServico()
        {
            var retorno = set_EnviaApolice();
        }

        public async Task set_EnviaApolice()
        {
            try
            {
                string url = "https://portal.carsystem.com/WSOInsurance/InserirSeguro";
                HttpClientHandler handler = new HttpClientHandler();
                HttpClient httpClient = new HttpClient(handler);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
                HttpResponseMessage response = await httpClient.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.OK)
                {

                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }


    }
}
