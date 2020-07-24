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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using tt_net_sdk;
using gateway.TT;
using tt.messaging.ttus;
using gateway.Services;
using Gateway.Utils;
using Datamodel.Csharp.Utils;
using Datamodel.Csharp.Models;
using STAN.Client;
using App.Metrics;
using Adaptive.SimpleBinaryEncoding;
using MessageHeader = Adaptive.SimpleBinaryEncoding.Ir.Generated.MessageHeader;
using TradeDirection = Datamodel.Generated.Csharp.Sbe.TradeDirection;
using Events = Datamodel.Generated.Csharp.Models.Events;
using Enums = Datamodel.Generated.Csharp.Models.Enums;
using TTUtil = gateway.TT.Util;
using gateway.Config;
using Datamodel.Generated.Csharp.Models.Events;
using gateway.TT.Util;
using Google.Protobuf.WellKnownTypes;
using Google.Protobuf;

namespace TTNETAPI_Sample_Console_AlgoOrderRouting
{
    public class TTNetApiFunctions
    {
        // Declare the API objects
        private TTAPI m_api = null;
        private IDataStream _data_stream;
        private NatsDataStream _nats = null;
        private NatsOrderStream _natsOrders = null;
        private IOrderStream _ordersSub = null;
        private ManualResetEvent mre = new ManualResetEvent(false);
        private InstrumentLookup m_instrLookupRequest = null;
        private PriceSubscription m_priceSubscription = null;
        private TimeAndSalesSubscription m_tasSubscription = null;
        private tt_net_sdk.WorkerDispatcher m_disp = null;
        private TradeSubscription m_tradeSubscription = null;
        private AlgoTradeSubscription m_algoTradeSubscription = null;
        private AlgoLookupSubscription m_algoLookupSubscription = null;
        private IReadOnlyCollection<tt_net_sdk.Account> m_accounts = null;
        private Instrument m_instrument = null;
        private Algo m_algo = null;
        private object m_Lock = new object();
        private bool m_isDisposed = false;
        private Price m_price = Price.Empty;

        // Instrument Information 
        private Dictionary<string, Instrument> instrumentDict = new Dictionary<string, Instrument>();
        private Dictionary<string, InstrumentLookup> instrumentLookupDict = new Dictionary<string, InstrumentLookup>();
        private Dictionary<string, PriceSubscription> priceSubScriptionDict = new Dictionary<string, PriceSubscription>();
        private Dictionary<string, TimeAndSalesSubscription> timeAndSalesSubscriptionDict = new Dictionary<string, TimeAndSalesSubscription>();

        //private readonly string m_market = "CME";
        //private readonly string m_product = "CL";
        //private readonly string m_prodType = "Future";
        //private readonly string m_alias = "CL Aug20";        
        private readonly string m_market = "0";
        private readonly string m_product = "ASE";
        private readonly string m_prodType = "Synthetic";
        private readonly string m_alias = "NGHJ";

        private readonly string m_alias_2 = "CLZZ";
        private readonly string m_alias_3 = "BRNZZ";

        private readonly ulong m_instrumentId = 17173196713478330956;
        private IStanSubscription _orderReceivedSub;
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>  Attach the worker Dispatcher </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void Start(tt_net_sdk.TTAPIOptions apiConfig)
        {
            _nats = new NatsDataStream("test-cluster", "gateway-data-client");
            _natsOrders = new NatsOrderStream("test-cluster", "gateway-orders-client");
            //natsOrders.SubscribeOrderRequests += new EventHandler<Events.SingleOrderUpdate>(HandleOrderRequestReceived)
            m_disp = tt_net_sdk.Dispatcher.AttachWorkerDispatcher();
            m_disp.DispatchAction(() =>
            {
                Init(apiConfig);
            });

            m_disp.Run();
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Initialize the API </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void Init(tt_net_sdk.TTAPIOptions apiConfig)
        {
            ApiInitializeHandler apiInitializeHandler = new ApiInitializeHandler(ttNetApiInitHandler);
            TTAPI.ShutdownCompleted += TTAPI_ShutdownCompleted;

            //For Algo Orders
            apiConfig.AlgoUserDisconnectAction = UserDisconnectAction.Cancel;
            TTAPI.CreateTTAPI(tt_net_sdk.Dispatcher.Current,apiConfig,apiInitializeHandler);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for status of API initialization. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void ttNetApiInitHandler(TTAPI api,ApiCreationException ex)
        {
            if(ex == null)
            {
                Console.WriteLine("TT.NET SDK INITIALIZED");

                // Authenticate your credentials
                m_api = api;
                m_api.TTAPIStatusUpdate += new EventHandler<TTAPIStatusUpdateEventArgs>(m_api_TTAPIStatusUpdate);
                m_api.Start();
            }
            else if(ex.IsRecoverable)
            {
                // Initialization failed but retry is in progress...
            }
            else
            {
                Console.WriteLine("TT.NET SDK Initialization Failed: {0}",ex.Message);
                Dispose();
            }
        }

        void StartAlgo()
        {
            m_price = Price.FromTick(m_instrument, 1900, Rounding.Nearest);

            while (! m_price.IsValid || m_algo ==null)
                mre.WaitOne();

            // To retrieve the list of parameters valid for the Algo you can call algo.AlgoParameters;
            // Construct a dictionary of the parameters and the values to send out 
            Dictionary<string,object> algo_userparams = new Dictionary<string,object>
                {
                    {"Ignore Market State",     true},
                };

            var lines = algo_userparams.Select(kvp => kvp.Key + ": " + kvp.Value.ToString());
            Console.WriteLine(string.Join(Environment.NewLine,lines));

            OrderProfile algo_op = m_algo.GetOrderProfile(m_instrument);

            algo_op.LimitPrice = m_price;
            algo_op.OrderQuantity = Quantity.FromDecimal(m_instrument,5); ;
            algo_op.Side = OrderSide.Buy;
            algo_op.OrderType = OrderType.Limit;
            algo_op.Account = m_accounts.ElementAt(0);
           //algo_op.UserParameters = algo_userparams;
            m_algoTradeSubscription.SendOrder(algo_op);
        }

        // <summary>
        //     Create a new order from the specified create order event
        // </summary>
        // <param name="o"></param>
        public void CreateOrder(SingleOrderUpdate o)
        {
            OrderResponseEvent response;
            try
            {
                Instrument orderInstrument = instrumentDict[o.Alias];

                Console.WriteLine(
                    "Order Created {0} {1} {3} {4} {5}", 
                    m_accounts.ElementAt(0), 
                    orderInstrument.InstrumentDetails.Alias, 
                    o.InternalOrderId,
                    Price.FromTick(orderInstrument, o.LimitPriceTicks),
                    (BuySell)o.Side,
                    Quantity.FromDecimal(orderInstrument, o.Qty));

                OrderProfile op = new OrderProfile(orderInstrument);
                op.LimitPrice = Price.FromTick(orderInstrument, o.LimitPriceTicks);
                op.OrderQuantity = Quantity.FromDecimal(orderInstrument, o.Qty);
                op.Side = (o.Side > 0) ? OrderSide.Sell: OrderSide.Buy;
                op.OrderType = OrderType.Limit;
                op.Account = m_accounts.ElementAt(0);
                op.OrderTag = o.InternalOrderId;
                m_tradeSubscription.SendOrder(op);
            }

            catch (Exception ex)
            {
                Console.WriteLine(ex.Message, "CREATE ORDER ERROR");
                response = new OrderResponseEvent
                {
                    Status = false,
                    GatewayTimestamp = Timestamp.FromDateTime(Clock.UtcNow()),
                    InternalOrderId = o.InternalOrderId,
                };
                _natsOrders.Send(response);
            }
        }

        // <summary>
        //     Update an existing order create order event
        // </summary>
        // <param name="o"></param>
        public void UpdateOrder(SingleOrderUpdate o)
        {
            try
            {
                Instrument orderInstrument = instrumentDict[o.Alias];

                Console.WriteLine(
                    "Order Update Reqest: {0} {1} {3} {4} {5}",
                    m_accounts.ElementAt(0),
                    orderInstrument.InstrumentDetails.Alias,
                    o.InternalOrderId,
                    Price.FromTick(orderInstrument, o.LimitPriceTicks),
                    (BuySell)o.Side,
                    Quantity.FromDecimal(orderInstrument, o.Qty));

                if (m_tradeSubscription.Orders.ContainsKey(o.ExternalOrderId))
                {
                    OrderProfile op = m_tradeSubscription.Orders[o.ExternalOrderId].GetOrderProfile();
                    op.Action = OrderAction.Change;
                    op.LimitPrice = Price.FromTick(orderInstrument, o.LimitPriceTicks);
                    op.OrderQuantity = Quantity.FromDecimal(orderInstrument, o.Qty);
                    //m_tradeSubscription.SendOrder(op);

                    Console.WriteLine("Change price from {0} to {1}", o.LimitPriceTicks, op.LimitPrice);

                    if (!m_tradeSubscription.SendOrder(op))
                    {
                        Console.WriteLine("ORDER UPDATE ERROR: " + o.Alias + " " + op.OrderQuantity.ToString() + "@" + op.LimitPrice.ToString() + " SOK=" + op.SiteOrderKey);
                    }
                    else
                    {
                        Console.WriteLine("ORDER UPDATED: " + o.Alias + " " + op.OrderQuantity.ToString() + "@" + op.LimitPrice.ToString() + " SOK=" + op.SiteOrderKey);
                    }
                }

                else
                {
                    Console.WriteLine("ORDER ID NOT FOUND: {0}\nAvailable Keys:", o.ExternalOrderId, m_tradeSubscription.Orders.Keys);

                    List<string> keyList = new List<string>(this.m_tradeSubscription.Orders.Keys);

                    foreach (string key in keyList)
                    {
                        Console.WriteLine(key);
                    }
                }

            }

            catch (Exception ex)
            {
                Console.WriteLine("Error while updating order", ex.Message);
            }
        }

        // <summary>
        //     Delete an existing order order event
        // </summary>
        // <param name="o"></param>
        public void DeleteOrder(SingleOrderUpdate o)
        {
            try
            {
                Instrument orderInstrument = instrumentDict[o.Alias];

                Console.WriteLine(
                    "Delete Order Request: {0} {1} {3} {4} {5}",
                    m_accounts.ElementAt(0),
                    orderInstrument.InstrumentDetails.Alias,
                    o.InternalOrderId,
                    Price.FromTick(orderInstrument, o.LimitPriceTicks),
                    (BuySell)o.Side,
                    Quantity.FromDecimal(orderInstrument, o.Qty));

                OrderProfile op = m_tradeSubscription.Orders[o.ExternalOrderId].GetOrderProfile();
                op.Action = OrderAction.Delete;
                op.Account = m_accounts.ElementAt(0);
                m_tradeSubscription.SendOrder(op);

                Console.WriteLine("Change price from {0} to {1}", o.LimitPriceTicks, op.LimitPrice);

                if (!m_tradeSubscription.SendOrder(op))
                {
                    Console.WriteLine("DELETE ORDER ERROR: " + o.Alias + " " + op.OrderQuantity.ToString() + "@" + op.LimitPrice.ToString() + " SOK=" + op.SiteOrderKey);
                }
                else
                {
                    Console.WriteLine("Delete order succeeded.");
                }
            }

            catch (Exception ex)
            {
                Console.WriteLine("Error While Deleting Order", ex.Message);
            }
        }

        private void HandleOrderRequestReceived(object sender, StanMsgHandlerArgs e)
        {
            OrderResponseEvent response;
            try
            {
                Console.WriteLine("Received order");
                var ev = OrderUpdateEvent.Parser.ParseFrom(e.Message.Data);
                foreach (var o in ev.Orders)
                {
                    response = new OrderResponseEvent
                    {
                        Status = true,
                        GatewayTimestamp = Timestamp.FromDateTime(Clock.UtcNow()),
                        InternalOrderId = o.InternalOrderId,
                    };
                    switch (o.OrderAction)
                    {
                        case Datamodel.Generated.Csharp.Models.Enums.OrderAction.AddAction:
                            Console.WriteLine("CREATING ORDER: {0}", o.InternalOrderId);
                            CreateOrder(o);
                            break;
                        case Datamodel.Generated.Csharp.Models.Enums.OrderAction.ChangeAction:
                            Console.WriteLine("UPDATING ORDER: {0}", o.InternalOrderId);
                            UpdateOrder(o);
                            break;
                        case Datamodel.Generated.Csharp.Models.Enums.OrderAction.DeleteAction:
                            Console.WriteLine("DELETING ORDER: {0}", o.InternalOrderId);
                            DeleteOrder(o);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message, "Error while submitting orders to the message queue");
                response = new OrderResponseEvent
                {
                    Status = false,
                    GatewayTimestamp = Timestamp.FromDateTime(Clock.UtcNow())
                };
                _natsOrders.Send(response);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for status of authentication. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        public void m_api_TTAPIStatusUpdate(object sender,TTAPIStatusUpdateEventArgs e)
            {
            Console.WriteLine("TTAPIStatusUpdate: {0}",e);
            if(e.IsReady == false)
            {
                // TODO: Do any connection lost processing here
                return;
            }
            // TODO: Do any connection up processing here
            //       note: can happen multiple times with your application life cycle

            // can get status multiple times - do not create subscription if it exists
            //
            if(object.ReferenceEquals(m_instrLookupRequest,null) == false)
                return;

            MarketId marketKey = Market.GetMarketIdFromName(m_market);
            ProductType productType = Product.GetProductTypeFromName(m_prodType);

            // Create instrument subscriptions
            CreateInstrumentSubscription(m_market, m_prodType, m_product, "NG_HJ");
            CreateInstrumentSubscription(m_market, m_prodType, m_product, m_alias);
            CreateInstrumentSubscription(m_market, m_prodType, m_product, m_alias_2);
            CreateInstrumentSubscription(m_market, m_prodType, m_product, m_alias_3);

            // Get the accounts
            m_accounts = m_api.Accounts;

            _orderReceivedSub = _natsOrders.SubscribeOrderRequests(HandleOrderRequestReceived);

            // Create Global Trade Subscription
            m_tradeSubscription = new TradeSubscription(tt_net_sdk.Dispatcher.Current);
            m_tradeSubscription.OrderUpdated += new EventHandler<OrderUpdatedEventArgs>(m_tradeSubscription_OrderUpdated);
            m_tradeSubscription.OrderAdded += new EventHandler<OrderAddedEventArgs>(m_tradeSubscription_OrderAdded);
            m_tradeSubscription.OrderDeleted += new EventHandler<OrderDeletedEventArgs>(m_tradeSubscription_OrderDeleted);
            m_tradeSubscription.OrderFilled += new EventHandler<OrderFilledEventArgs>(m_tradeSubscription_OrderFilled);
            m_tradeSubscription.OrderRejected += new EventHandler<OrderRejectedEventArgs>(m_tradeSubscription_OrderRejected);
            m_tradeSubscription.OrderBookDownload += new EventHandler<OrderBookDownloadEventArgs>(m_tradeSubscription_OrderBookDownload);
            m_tradeSubscription.Start();
        }
        private void CreateInstrumentSubscription(string market, string prodType, string product, string alias)
        {
            MarketId marketKey = Market.GetMarketIdFromName(market);
            ProductType productType = Product.GetProductTypeFromName(prodType);

            // lookup an instrument
            var instLookup = new InstrumentLookup(tt_net_sdk.Dispatcher.Current, marketKey, productType, product, alias);

            instrumentLookupDict[alias] = instLookup;

            instLookup.OnData += m_instrLookupRequest_OnData;
            instLookup.GetAsync();
        }

        void m_instrLookupRequest_OnData(object sender, InstrumentLookupEventArgs e)
        {
            if (e.Event == ProductDataEvent.Found)
            {
                var instrument = e.InstrumentLookup.Instrument;
                var key = instrument.InstrumentDetails.Alias;

                // Instrument was found
                instrumentLookupDict[key] = e.InstrumentLookup;
                instrumentDict[key] = instrument;
                Console.WriteLine("Found: {0}", instrument);
                Console.WriteLine("Instrument ID: {0}", instrument.Key.InstrumentId);

                //// Subscribe for market Data
                var priceSubscription = new PriceSubscription(instrument, tt_net_sdk.Dispatcher.Current);
                priceSubscription = new PriceSubscription(instrument, tt_net_sdk.Dispatcher.Current);
                priceSubscription.Settings = new PriceSubscriptionSettings(PriceSubscriptionType.InsideMarket);
                //m_priceSubscription.FieldsUpdated += m_priceSubscription_FieldsUpdated;
                //m_priceSubscription.FieldsUpdated += HandlePriceDepthUpdate;
                priceSubscription.FieldsUpdated += HandleInsideMarketUpdate;
                priceSubscription.Start();
                priceSubScriptionDict[key] = priceSubscription;

                Console.WriteLine("Creating Time and Sales Subscription...", instrument);
                var tasSubscription = new TimeAndSalesSubscription(instrument, tt_net_sdk.Dispatcher.Current);
                tasSubscription.Update += m_tasSubscription_Updated;
                tasSubscription.Start();
                timeAndSalesSubscriptionDict[key] = tasSubscription;
                Console.WriteLine("Time and Sales Subscription Created", instrument);

            }
            else if (e.Event == ProductDataEvent.NotAllowed)
            {
                Console.WriteLine("Not Allowed : Please check your Token access");
            }
            else
            {
                // Instrument was not found and TT API has given up looking for it
                Console.WriteLine("Cannot find instrument: {0}", e.Message);
                Dispose();
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for price update. </summary>
        /// <param name="sender">   Source of the event. </param>
        /// <param name="e">        Fields updated event information. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_priceSubscription_FieldsUpdated(object sender, FieldsUpdatedEventArgs e)
        {
            if (e.Error != null)
            {
                Console.WriteLine("Unrecoverable price subscription error: {0}", e.Error.Message);
                Dispose();
            }
            else if (e.Fields.GetBestBidPriceField().Value != null)
            {
                m_price = e.Fields.GetBestBidPriceField().Value;
                mre.Set();


                //_nats.SendDepthPersisted(e.Fields.Instrument.Product.Alias, e.Fields.Instrument.Product.Market.MarketId, e.Fields.Instrument.Product.Type,
                //    Sbe.SerializeDepthEvent(e.Fields.ExchangeRecvTime, e.Fields.instrumentID, e.UpdateType, e.Fields.))

            }
        }
        public void HandleInsideMarketUpdate(object sender, FieldsUpdatedEventArgs e)
        {
            if (e.Error != null)
            {
                Console.WriteLine("Unrecoverable price subscription error: {0}", e.Error.Message);
                Dispose();
                return;
            }

            var ev = new InsideMarketEvent
            {
                Timestamp = Timestamp.FromDateTime(Clock.UtcNow()),
                UpdateType = (Datamodel.Generated.Csharp.Models.Enums.UpdateType)e.UpdateType,
                BestBidPrice = e.Fields.GetBestBidPriceField().Value.ToTicks(),
                BestBidQty = (int)e.Fields.GetBestBidQuantityField().Value.ToDecimal(),
                BestAskPrice = e.Fields.GetBestAskPriceField().Value.ToTicks(),
                BestAskQty = (int)e.Fields.GetBestBidQuantityField().Value.ToDecimal(),
                Market = e.Fields.Instrument.Product.Market.Name,
                Product = e.Fields.Instrument.Product.Name,
                ProductType = e.Fields.Instrument.Product.Type.ToString(),
                Alias = e.Fields.Instrument.InstrumentDetails.Alias,
            };
            _nats.SendInsideMarketPersisted(e.Fields.Instrument.Product.Alias, e.Fields.Instrument.Product.Market.MarketId, e.Fields.Instrument.Product.Type, ev.ToByteArray());
        }

            public void HandlePriceDepthUpdate(object sender, FieldsUpdatedEventArgs e)
        {
            var sub = sender as PriceSubscription;
            var eventTimeNs = Clock.UtcNowNanos();

            if (e.Error != null)
            {
                Console.WriteLine("Unrecoverable price subscription error: {0}", e.Error.Message);
                Dispose();
                return;
            }

            var inst = e.Fields.Instrument;

            var fieldCount = 0;
            
            try
            {
                Span<IntField> fields = stackalloc IntField[128];
                var statusUpdated = false;
                if (e.UpdateType == UpdateType.Snapshot)
                {
                    //we don't care about data more than 10 levels deep
                    var askDepthLevels = Math.Max(10, e.Fields.GetLargestCurrentDepthLevel(FieldId.BestAskPrice));
                    var bidDepthLevels = Math.Max(10, e.Fields.GetLargestCurrentDepthLevel(FieldId.BestBidPrice));
                    statusUpdated = true;
                    fields[0] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.SettlementPrice,
                        Value = e.Fields.GetSettlementPriceField().Value.ToTicks()
                    };
                    fields[1] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.ImbalanceQuantity,
                        Value = 0
                        //Value = (int) e.Fields.GetImbalanceQuantityField().Value
                    };
                    fields[2] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.MarketSide,
                        Value = (int)e.Fields.GetMarketSideField().Value
                    };
                    fields[3] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.SeriesStatus,
                        Value = (int)e.Fields.GetSeriesStatusField().Value
                    };
                    fields[4] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.TotalTradedQuantity,
                        Value = 0
                    };
                    fields[5] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.LastTradedQuantity,
                        Value = 0
                        // Value = (int)e.Fields.GetLastTradedQuantityField().Value if e.Fields.GetLastTradedQuantityField().HasValidValue
                    };
                    fields[6] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.HighPrice,
                        Value = e.Fields.GetHighPriceField().Value.ToTicks()
                    };
                    fields[7] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.LowPrice,
                        Value = e.Fields.GetLowPriceField().Value.ToTicks()
                    };
                    fields[8] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.OpenPrice,
                        Value = e.Fields.GetOpenPriceField().Value.ToTicks()
                    };
                    fields[9] = new IntField
                    {
                        Depth = 0,
                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.ClosePrice,
                        Value = e.Fields.GetClosePriceField().Value.ToTicks()
                    };
                    fieldCount = 10;
                    for (short i = 0; i < askDepthLevels; i++)
                    {
                        fields[fieldCount++] = new IntField
                        {
                            Depth = i,
                            FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestAskPrice,
                            Value = e.Fields.GetBestAskPriceField(i).Value.ToTicks()
                        };
                        fields[fieldCount++] = new IntField
                        {
                            Depth = i,
                            FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestAskQuantity,
                            Value = (int)e.Fields.GetBestAskQuantityField(i).Value.ToDecimal()
                        };
                        fields[fieldCount++] = new IntField
                        {
                            Depth = i,
                            FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.MergedAskCount,
                            Value = (int)e.Fields.GetMergedAskCountField(i).Value
                        };
                    }

                    for (short i = 0; i < bidDepthLevels; i++)
                    {
                        fields[fieldCount++] = new IntField
                        {
                            Depth = i,
                            FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestBidPrice,
                            Value = e.Fields.GetBestBidPriceField(i).Value.ToTicks()
                        };
                        fields[fieldCount++] = new IntField
                        {
                            Depth = i,
                            FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestBidQuantity,
                            Value = (int)e.Fields.GetBestBidQuantityField(i).Value
                        };
                        fields[fieldCount++] = new IntField
                        {
                            Depth = i,
                            FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.MergedBidCount,
                            Value = (int)e.Fields.GetMergedBidCountField(i).Value
                        };
                    }
                }
                else
                {
                    //we don't care about data more than 10 levels deep
                    var depthLevels = Math.Max(10, e.Fields.GetLargestCurrentDepthLevel());
                    for (short depth = 0; depth < depthLevels; depth++)
                    {
                        var changed = e.Fields.GetChangedFieldIds(depth);
                        foreach (var fid in changed)
                            switch (fid)
                            {
                                case FieldId.TotalTradedQuantity:
                                    fields[fieldCount++] = new IntField
                                    {
                                        Depth = 0,
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId
                                            .TotalTradedQuantity,
                                        Value = (int)e.Fields.GetTotalTradedQuantityField().Value
                                    };
                                    break;
                                case FieldId.LastTradedQuantity:
                                    fields[fieldCount++] = new IntField
                                    {
                                        Depth = 0,
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId
                                            .LastTradedQuantity,
                                        Value = (int)e.Fields.GetLastTradedQuantityField().Value
                                    };
                                    break;
                                case FieldId.HighPrice:
                                    fields[fieldCount++] = new IntField
                                    {
                                        Depth = 0,
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.HighPrice,
                                        Value = e.Fields.GetHighPriceField().Value.ToTicks()
                                    };
                                    break;
                                case FieldId.LowPrice:
                                    fields[fieldCount++] = new IntField
                                    {
                                        Depth = 0,
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.LowPrice,
                                        Value = e.Fields.GetLowPriceField().Value.ToTicks()
                                    };
                                    break;
                                case FieldId.OpenPrice:
                                    fields[fieldCount++] = new IntField
                                    {
                                        Depth = 0,
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.OpenPrice,
                                        Value = e.Fields.GetOpenPriceField().Value.ToTicks()
                                    };
                                    break;
                                case FieldId.ClosePrice:
                                    fields[fieldCount++] = new IntField
                                    {
                                        Depth = 0,
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.ClosePrice,
                                        Value = e.Fields.GetClosePriceField().Value.ToTicks()
                                    };
                                    break;
                                case FieldId.SettlementPrice:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.SettlementPrice,
                                        Depth = 0,
                                        Value = e.Fields.GetSettlementPriceField().Value.ToTicks()
                                    };
                                    break;
                                case FieldId.ImbalanceQuantity:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId =
                                            (Datamodel.Generated.Csharp.Sbe.FieldId) FieldId.ImbalanceQuantity,
                                        Depth = 0,
                                        Value = 0
                                    };
                                    break;
                                case FieldId.MarketSide:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.MarketSide,
                                        Depth = 0,
                                        Value = (int)e.Fields.GetMarketSideField().Value
                                    };
                                    break;
                                case FieldId.CurrentSessionId:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.CurrentSessionId,
                                        Depth = 0,
                                        Value = 0
                                    };
                                    break;
                                case FieldId.SeriesStatus:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.SeriesStatus,
                                        Depth = 0,
                                        Value = (int)e.Fields.GetSeriesStatusField().Value
                                    };
                                    statusUpdated = true;
                                    break;
                                case FieldId.BestAskPrice:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestAskPrice,
                                        Depth = depth,
                                        Value = e.Fields.GetBestAskPriceField(depth).Value.ToTicks()
                                    };
                                    break;
                                case FieldId.BestAskQuantity:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestAskQuantity,
                                        Depth = depth,
                                        Value = (int)e.Fields.GetBestAskQuantityField(depth).Value
                                    };
                                    break;
                                case FieldId.MergedAskCount:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.MergedAskCount,
                                        Depth = depth,
                                        Value = (int)e.Fields.GetMergedAskCountField(depth).Value
                                    };
                                    break;
                                case FieldId.BestBidPrice:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestBidPrice,
                                        Depth = depth,
                                        Value = e.Fields.GetBestBidPriceField(depth).Value.ToTicks()
                                    };
                                    break;
                                case FieldId.BestBidQuantity:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.BestBidQuantity,
                                        Depth = depth,
                                        Value = (int)e.Fields.GetBestBidQuantityField(depth).Value
                                    };
                                    break;
                                case FieldId.MergedBidCount:
                                    fields[fieldCount++] = new IntField
                                    {
                                        FieldId = (Datamodel.Generated.Csharp.Sbe.FieldId)FieldId.MergedBidCount,
                                        Depth = depth,
                                        Value = (int)e.Fields.GetMergedBidCountField(depth).Value
                                    };
                                    break;
                            }
                    }
                }
                _nats.SendDepthPersisted(inst.Product.Alias, inst.Product.Key.MarketId,
                        inst.Product.Type,
                        Sbe.SerializeDepthEvent(eventTimeNs, (int)inst.Product.Key.MarketId,
                            (Datamodel.Generated.Csharp.Sbe.UpdateType)e.UpdateType, fieldCount, fields));

                //if (statusUpdated)
                //{
                //    _nats.SendStatusPersisted(inst.Product.Alias, inst.Product.Key.MarketId,
                //        inst.Product.Type,
                //        Sbe.SerializeStatusEvent(eventTimeNs, (int) inst.Product.Key.MarketId,
                //            (Datamodel.Generated.Csharp.Sbe.TradingStatus)e.Fields.GetSeriesStatusField().Value));
                //}

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message, "Failed to process depth event. Instrument {0}, update type {1}, fields len {2}", inst.Product.Alias, e.UpdateType.ToString("G"), fieldCount);
            }
            
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for time and sales update. </summary>
        /// <param name="sender">   Source of the event. </param>
        /// <param name="e">        Fields updated event information. </param>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_tasSubscription_Updated(object sender, TimeAndSalesEventArgs e)
        {
            //Console.WriteLine("New TAS Event");
            if (e.Error == null)
            {
                // More than one LTP/LTQ may be received in a single event
                foreach (TimeAndSalesData ts in e.Data)
                {
                    var localTsNs = Clock.UtcNowNanos();
                    var exchangeTsNs = Clock.UnixNanosFromDateTime(ts.TimeStamp.ToUniversalTime());


                    // TODO Create a simple container class for TAS event and then serialize to JSON
                    _nats.SendTasPersisted(ts.Instrument.Product.Alias, ts.Instrument.Product.Market.MarketId, ts.Instrument.Product.Type,
                        Sbe.SerializeTasEvent(localTsNs, exchangeTsNs, (int) ts.Instrument.Key.InstrumentId, (TradeDirection)ts.Direction,
                        (int) ts.TradeQuantity.Value, ts.TradePrice.ToTicks(), ts.IsOverTheCounter));

                    //Console.WriteLine("\n[{0}] {1} isOTC={2} isImplied={3} isLegTrade={4} {5} {6} @ {7}", ts.TimeStamp, ts.Instrument.Name, ts.IsOverTheCounter, ts.IsImplied, ts.IsLegTrade, ts.Direction, ts.TradePrice, ts.TradeQuantity);
                }

            }
            else
            {
                if (e.Error != null)
                {
                    Console.WriteLine("Unrecoverable Time and Sales subscription error: {0}", e.Error.Message);
                    tt_net_sdk.TimeAndSalesSubscription ts = (tt_net_sdk.TimeAndSalesSubscription)sender;
                    Console.WriteLine("Unrecoverable price subscription error: {0}", e.Error.Message);
                    ts.Dispose();
                }
            }
        }
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for order book download complete. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_tradeSubscription_OrderBookDownload(object sender, OrderBookDownloadEventArgs e)
        {
            Console.WriteLine("Orderbook downloaded...");
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for order rejection. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_tradeSubscription_OrderRejected(object sender, OrderRejectedEventArgs e)
        {
            Console.WriteLine("\nOrderRejected for : [{0}]", e.Order.SiteOrderKey);
            var ev = new OrderResponseEvent
            {
                Status = true,
                InternalOrderId = e.Order.SiteOrderKey,
                Order = TTUtils.FromTTOrderToProto(e.Order, (int)e.Order.InstrumentKey.InstrumentId),
                GatewayTimestamp = Timestamp.FromDateTime(Clock.UtcNow()),
            };
            _natsOrders.Send(ev);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for order fills. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_tradeSubscription_OrderFilled(object sender, OrderFilledEventArgs e)
        {
            var ev = new FillResponseEvent
            {
                Fill = TTUtils.FromTTFill(e.Fill, Timestamp.FromDateTime(e.Fill.TransactionDateTime.ToUniversalTime()), (int)e.Fill.InstrumentKey.InstrumentId),

            };
            Console.WriteLine("FILL {0}", ev.ToString());
            _natsOrders.Send(ev);

            if (e.FillType == tt_net_sdk.FillType.Full)
            {
                Console.WriteLine("\nOrderFullyFilled [{0}]: {1}@{2}", e.Fill.SiteOrderKey, e.Fill.Quantity, e.Fill.MatchPrice);
            }
            else
            {
                Console.WriteLine("\nOrderPartiallyFilled [{0}]: {1}@{2}", e.Fill.SiteOrderKey, e.Fill.Quantity, e.Fill.MatchPrice);
            }
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for order deletion. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_tradeSubscription_OrderDeleted(object sender, OrderDeletedEventArgs e)
        {
            var ev = new OrderResponseEvent
            {
                Status=true,
                InternalOrderId = string.IsNullOrEmpty(e.OldOrder.OrderTag) ? e.OldOrder.SiteOrderKey : e.OldOrder.OrderTag,
                Order = TTUtils.FromTTOrderToProto(e.DeletedUpdate, (int)e.DeletedUpdate.InstrumentKey.InstrumentId),
                GatewayTimestamp= Timestamp.FromDateTime(Clock.UtcNow()),
            };
            _natsOrders.Send(ev);
            Console.WriteLine("\nOrderDeleted [{0}] , Message : {1}", e.OldOrder.SiteOrderKey, e.Message);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for order addition. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_tradeSubscription_OrderAdded(object sender, OrderAddedEventArgs e)
        {
            var ev = new OrderResponseEvent
            {
                Status = true,
                InternalOrderId = e.Order.ExchangeOrderId,
                IsCancelReplace = false,
                GatewayTimestamp = Timestamp.FromDateTime(Clock.UtcNow()),
                Order = TTUtils.FromTTOrderToProto(e.Order, (int)e.Order.InstrumentKey.InstrumentId),
            };

            _natsOrders.Send(ev);

            if (e.Order.IsSynthetic)
                Console.WriteLine("\nPARENT OrderAdded [{0}] with Synthetic Status : {1} ", e.Order.SiteOrderKey, e.Order.SyntheticStatus.ToString());
            else
                Console.WriteLine("\nOrderAdded [{0}] {1} {2}: {3} {4}", e.Order.SiteOrderKey, e.Order.InstrumentDetails.Id, e.Order.BuySell, e.Order.ToString(), e.Order.ExchangeOrderId.ToString());
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////
        /// <summary>   Event notification for order update. </summary>
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        void m_tradeSubscription_OrderUpdated(object sender, OrderUpdatedEventArgs e)
        {
            if (e.NewOrder.ExecutionType == ExecType.Restated)
            {
                Console.WriteLine("\nAlgo Order Restated [{0}] with Synthetic Status : {1} ", e.NewOrder.SiteOrderKey, e.NewOrder.SyntheticStatus.ToString());

            }
            else
            {
                var ev = new OrderResponseEvent
                {
                    Status = true,
                    InternalOrderId = string.IsNullOrEmpty(e.NewOrder.OrderTag) ? e.NewOrder.SiteOrderKey : e.NewOrder.OrderTag,
                    OldInternalOrderId = string.IsNullOrEmpty(e.OldOrder.OrderTag) ? e.OldOrder.SiteOrderKey : e.OldOrder.OrderTag,
                    Order = TTUtils.FromTTOrderToProto(e.NewOrder, (int)e.NewOrder.InstrumentKey.InstrumentId),
                    GatewayTimestamp = Timestamp.FromDateTime(Clock.UtcNow()),
                };
                _natsOrders.Send(ev);
                if (e.NewOrder.IsSynthetic)
                {
                    Console.WriteLine("ORDER UPDATED [{0}] ALIAS: {1} {2}: {3} ID: {4} TYPE: {5} NAME: {6}", 
                    e.NewOrder.SiteOrderKey,
                    e.NewOrder.InstrumentKey.Alias,
                    e.NewOrder.BuySell, 
                    e.NewOrder.ToString(), 
                    e.NewOrder.InstrumentKey.ProductKey.MarketId.ToString(), 
                    e.NewOrder.InstrumentKey.ProductKey.Type.ToString(),
                    e.NewOrder.InstrumentKey.ProductKey.Name.ToString()
                    );
                }
                
            }
                
        }

        public void Dispose()
        {
            lock (m_Lock)
            {
                if (!m_isDisposed)
                {
                    // Unattached callbacks and dispose of all subscriptions
                    if (m_instrLookupRequest != null)
                    {
                        m_instrLookupRequest.OnData -= m_instrLookupRequest_OnData;
                        m_instrLookupRequest.Dispose();
                        m_instrLookupRequest = null;
                    }

                    foreach (KeyValuePair<string, InstrumentLookup> kvp in instrumentLookupDict)
                    {
                        var sub = kvp.Value;
                        sub.OnData -= m_instrLookupRequest_OnData;
                        sub.Dispose();
                    }

                    if (m_priceSubscription != null)
                    {
                        m_priceSubscription.FieldsUpdated -= m_priceSubscription_FieldsUpdated;
                        //m_priceSubscription.FieldsUpdated -= HandlePriceDepthUpdate;
                        m_priceSubscription.FieldsUpdated -= HandleInsideMarketUpdate;
                        m_priceSubscription.Dispose();
                        m_priceSubscription = null;
                    }

                    foreach (KeyValuePair<string, PriceSubscription> kvp in priceSubScriptionDict)
                    {
                        var sub = kvp.Value;
                        //sub.FieldsUpdated -= HandlePriceDepthUpdate;
                        sub.FieldsUpdated -= HandleInsideMarketUpdate;
                        sub.Dispose();
                        sub = null;
                    }

                    foreach (KeyValuePair<string, TimeAndSalesSubscription> kvp in timeAndSalesSubscriptionDict)
                    {
                        var sub = kvp.Value;
                        sub.Update -= m_tasSubscription_Updated;
                        sub.Dispose();
                        sub = null;

                    }

                    if (m_tradeSubscription != null)
                    {
                        m_tradeSubscription.OrderUpdated -= m_tradeSubscription_OrderUpdated;
                        m_tradeSubscription.OrderAdded -= m_tradeSubscription_OrderAdded;
                        m_tradeSubscription.OrderDeleted -= m_tradeSubscription_OrderDeleted;
                        m_tradeSubscription.OrderFilled -= m_tradeSubscription_OrderFilled;
                        m_tradeSubscription.OrderRejected -= m_tradeSubscription_OrderRejected;
                        m_tradeSubscription.Dispose();
                        m_tradeSubscription = null;
                    }

                    m_isDisposed = true;
                }

                TTAPI.Shutdown();
            }
        }

        public void TTAPI_ShutdownCompleted(object sender, EventArgs e)
        {
            // Dispose of any other objects / resources
            Console.WriteLine("TTAPI shutdown completed");
        }
    }
}

