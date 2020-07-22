﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using tt_net_sdk;

namespace TT_SDK_GATEWAY
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Add your app secret Key here. It looks like: 00000000-0000-0000-0000-000000000000:00000000-0000-0000-0000-000000000000
                string appSecretKey = "Your App Key";

                //Set the environment the app needs to run in here
                tt_net_sdk.ServiceEnvironment environment = tt_net_sdk.ServiceEnvironment.UatCert;

                tt_net_sdk.TTAPIOptions apiConfig = new tt_net_sdk.TTAPIOptions(
                     environment,
                     appSecretKey,
                     5000);

                // Start the TT API on the same thread
                TTNetApiFunctions tf = new TTNetApiFunctions();

                Thread workerThread = new Thread(() => tf.Start(apiConfig));
                workerThread.Name = "TT NET SDK Thread";
                workerThread.Start();

                while (true)
                {
                    string input = System.Console.ReadLine();
                    if (input == "q")
                        break;
                }
                tf.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\n" + e.StackTrace);
            }

        }
    }
}
