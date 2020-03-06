using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace Pulling
{
    static class Program
    {
        /// <summary>
        /// 
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            if (System.Diagnostics.Debugger.IsAttached)
            {
                #if DEBUG // debugando como DEBUG
                var service = new Service1();
                service.StartDebug();
                System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
                #endif // debugando como Release
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new Service1()
                };

                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}
