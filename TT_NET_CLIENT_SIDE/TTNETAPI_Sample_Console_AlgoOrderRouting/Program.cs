// **********************************************************************************************************************
//
//	Copyright © 2005-2020 Trading Technologies International, Inc.
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
using System.Threading;
using System.Threading.Tasks;


namespace TTNETAPI_Sample_Console_AlgoOrderRouting
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Add your app secret Key here. It looks like: 00000000-0000-0000-0000-000000000000:00000000-0000-0000-0000-000000000000
                //string appSecretKey = "ab8817fb-1d6d-d063-ef97-0e9344df637e:1c3321b1-2e92-8f36-2ee3-0c90c5cfc7eb";
                //tt_net_sdk.ServiceEnvironment environment = tt_net_sdk.ServiceEnvironment.ProdSim;

                //string appSecretKey = "a2d5c54f-5606-e305-528b-4f916a16ca1f:57b8b014-e20a-9365-79b2-2b683aa4954c";
                string appSecretKey = "d544d3c2-28cc-b93f-2faf-324097b6c3d7:4d3438bc-c7f0-8639-19fd-2309a27c7151";
                tt_net_sdk.ServiceEnvironment environment = tt_net_sdk.ServiceEnvironment.ProdLive;

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
