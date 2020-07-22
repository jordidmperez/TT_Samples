// **********************************************************************************************************************
//
//	Copyright © 2005-2019 Trading Technologies International, Inc.
//	All Rights Reserved Worldwide
//
// 	* * * S T R I C T L Y   P R O P R I E T A R Y * * *
//
//  WARNING: This file and all related programs (including any computer programs, example programs, and all source code) 
//  are the exclusive property of Trading Technologies International, Inc. (“TT”), are protected by copyright law and 
//  international treaties, and are for use only by those with the express written permission from TT.  Unauthorized 
//  possession, reproduction, distribution, use or disclosure of this file and any related program (or document) derived 
//  from it is prohibited by State and Federal law, and by local law outside of the U.S. and may result in severe civil 
//  and criminal penalties.
//
// ************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using tt_setup_sdk;

namespace TTSETUPSDK_Sample_Console
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                //Add your app secret Key here . The app_key looks like : 00000000-0000-0000-0000-000000000000:00000000-0000-0000-0000-000000000000
                string appSecretKey = "401e4f94-3714-8c8b-5cf4-b28ec27292e1:4b20046d-9d94-77ad-2681-6d28b18182ab";

                //Set the environment the app needs to run in here
                tt_setup_sdk.ServiceEnvironment environment = tt_setup_sdk.ServiceEnvironment.UatCert;

                tt_setup_sdk.TTSetupSDKOptions apiConfig = new tt_setup_sdk.TTSetupSDKOptions(
                     environment,
                     appSecretKey);

                // Start the TT SETUP SDK on the same thread
                TTSetUpFunctions tf = new TTSetUpFunctions();

                Thread workerThread = new Thread(() => tf.Start(apiConfig));
                workerThread.Name = "TT SETUP SDK Thread";
                workerThread.Start();

                while (true)
                {
                    string input = System.Console.ReadLine();

                    if (input == "a")
                        tf.GetAllAccounts();

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
