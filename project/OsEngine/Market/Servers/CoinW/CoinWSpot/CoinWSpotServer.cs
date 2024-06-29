using Newtonsoft.Json;
using OsEngine.Entity;
using OsEngine.Language;
using OsEngine.Logging;
using OsEngine.Market.Servers.CoinW.CoinWSpot.Json;
using OsEngine.Market.Servers.Entity;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using WebSocket4Net;

namespace OsEngine.Market.Servers.CoinW.CoinWSpot
{
    public class CoinWSpotServer : AServer
    {
        public CoinWSpotServer()
        {
            CoinWSpotServerRealization realization = new CoinWSpotServerRealization();
            ServerRealization = realization;

            CreateParameterString(OsLocalization.Market.ServerParamPublicKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamSecretKey, "");
            CreateParameterPassword(OsLocalization.Market.ServerParamPassphrase, "");
        }
    }

    public class CoinWSpotServerRealization : IServerRealization
    {
        private readonly string host = "https://www.coinw.com";
        private readonly HttpClient httpClient = new HttpClient();
        private readonly Dictionary<string, Security> subscribedSecurities = new Dictionary<string, Security>();

        private WebSocketInformation webSocketInformation;
        private WebSocket webSocket;
        private ConcurrentQueue<string> webSocketPublicMessages;
        private string socketLocker = "webSocketLockerCoinW";
        private Portfolio portfolio;

        public CoinWSpotServerRealization()
        {
            ServerStatus = ServerConnectStatus.Disconnect;

            Thread sendPingWebSocket = new Thread(SendPingWebSocket);
            sendPingWebSocket.IsBackground = true;
            sendPingWebSocket.Name = "SendPingWebSocketCoinWSpot";
            sendPingWebSocket.Start();

            Thread publicMessageReader = new Thread(PublicMessageReader);
            publicMessageReader.IsBackground = true;
            publicMessageReader.Name = "PublicMessageReaderCoinWSpot";
            publicMessageReader.Start();
        }

        private void PublicMessageReader()
        {
            Thread.Sleep(1000);

            while (true)
            {
                try
                {
                    if (ServerStatus == ServerConnectStatus.Disconnect)
                    {
                        Thread.Sleep(2000);
                        continue;
                    }

                    if (webSocketPublicMessages.IsEmpty)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    if (webSocketPublicMessages.TryDequeue(out string message))
                    {
                        // А вот тута парсим уже...
                    }

                }
                catch (Exception ex)
                {
                    Thread.Sleep(2000);
                    SendLogMessage(ex.ToString(), LogMessageType.Error);
                }
            }
        }

        private void SendPingWebSocket()
        {
            int pingInterval = 10000;
            while (true)
            {
                try
                {
                    if (webSocketInformation != null)
                    {
                        pingInterval = Convert.ToInt32(webSocketInformation.pingInterval);
                    }

                    Thread.Sleep(pingInterval);

                    if (webSocket != null && webSocket.State == WebSocketState.Open)
                    {
                        lock (socketLocker)
                        {
                            webSocket.Send("2"); // Socket.IO
                        }
                    }
                }
                catch (Exception e)
                {
                    SendLogMessage(e.ToString(), LogMessageType.Error);
                }
            }
        }

        public DateTime ServerTime { get; set; }

        public void Connect()
        {
            publicKey = ((ServerParameterString)ServerParameters[0]).Value;
            secretKey = ((ServerParameterPassword)ServerParameters[1]).Value;
            passphrase = ((ServerParameterPassword)ServerParameters[2]).Value;

            if (string.IsNullOrEmpty(publicKey) || string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(passphrase))
            {
                SendLogMessage("Can`t run CoinW Spot connector. No keys or passphrase", LogMessageType.Error);
                return;
            }

            try
            {
                HttpResponseMessage responseMessage = httpClient.GetAsync(host + "/pusher/public-token").Result;

                if (responseMessage.IsSuccessStatusCode)
                {
                    string responseContent = responseMessage.Content.ReadAsStringAsync().Result;
                    ResponseMessageRest<WebSocketInformation> responseMessageRest = JsonConvert.DeserializeObject<ResponseMessageRest<WebSocketInformation>>(responseContent);

                    if (responseMessageRest == null)
                    {
                        SendLogMessage("Can`t run CoinW Spot connector. No identity information required for WebSocket connection", LogMessageType.Error);
                        return;
                    }

                    if (responseMessageRest.code != 200.ToString())
                    {
                        SendLogMessage("Can`t run CoinW Spot connector. " + "Code: " + responseMessageRest.code, LogMessageType.Error);
                        return;
                    }

                    webSocketInformation = responseMessageRest.data;

                    webSocketPublicMessages = new ConcurrentQueue<string>();

                    CreateWebSocketConnection();

                    ServerStatus = ServerConnectStatus.Connect;
                    ConnectEvent();
                }
                else
                {
                    SendLogMessage("Connection cannot be open. CoinW. Error request", LogMessageType.Error);
                    ServerStatus = ServerConnectStatus.Disconnect;
                    DisconnectEvent();
                }
            }
            catch (Exception ex)
            {
                SendLogMessage(ex.ToString(), LogMessageType.Error);
                SendLogMessage("Connection can be open. CoinW. Error request", LogMessageType.Error);
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }
        }

        private void CreateWebSocketConnection()
        {
            string endpoint = webSocketInformation.endpoint;
            string token = webSocketInformation.token;
            //string pingInterval = webSocketInformation.pingInterval;

            string url = $"{endpoint}/socket.io/?token={token}&EIO=3&transport=websocket";

            webSocket = new WebSocket(url);
            //webSocket.EnableAutoSendPing = true;
            //webSocket.AutoSendPingInterval = Convert.ToInt32(pingInterval);

            webSocket.Opened += WebSocket_Opened;
            webSocket.Closed += WebSocket_Closed;
            webSocket.MessageReceived += WebSocket_MessageReceived;
            webSocket.Error += WebSocket_Error;

            webSocket.Open();
        }

        private void WebSocket_Opened(object sender, EventArgs e)
        {
            // Server uses Socket.IO https://socket.io/
            webSocket.Send("2probe");
            webSocket.Send("5");

            SendLogMessage("Websocket connection open", LogMessageType.System);
        }

        private void WebSocket_Closed(object sender, EventArgs e)
        {
            if (ServerStatus != ServerConnectStatus.Disconnect)
            {
                SendLogMessage("Connection Closed by CoinW. WebSocket Closed Event", LogMessageType.Error);
                ServerStatus = ServerConnectStatus.Disconnect;
                DisconnectEvent();
            }
        }

        private void WebSocket_MessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                if (e == null || webSocketPublicMessages == null)
                {
                    return;
                }

                string message = e.Message;
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }
                if (message.StartsWith("3")) // Pong message Socket.IO
                {
                    return;
                }
                if (message.StartsWith("42")) // Event message Socket.IO
                {
                    webSocketPublicMessages.Enqueue(message);
                }
            }
            catch (Exception ex)
            {
                SendLogMessage(ex.ToString(), LogMessageType.Error);
            }
        }

        private void WebSocket_Error(object sender, SuperSocket.ClientEngine.ErrorEventArgs e)
        {
            if (e.Exception != null)
            {
                SendLogMessage(e.Exception.ToString(), LogMessageType.Error);
            }
        }

        public void Dispose()
        {

        }

        public event Action ConnectEvent;

        public event Action DisconnectEvent;

        public List<IServerParameter> ServerParameters { get; set; }

        private string publicKey;

        private string secretKey;

        private string passphrase;

        public ServerType ServerType
        {
            get { return ServerType.CoinWSpot; }
        }

        public ServerConnectStatus ServerStatus { get; set; }

        public event Action<List<Security>> SecurityEvent;
        public event Action<List<Portfolio>> PortfolioEvent;
        public event Action<MarketDepth> MarketDepthEvent;
        public event Action<Trade> NewTradesEvent;
        public event Action<Order> MyOrderEvent;
        public event Action<MyTrade> MyTradeEvent;

        public void CancelAllOrders()
        {
            throw new NotImplementedException();
        }

        public void CancelAllOrdersToSecurity(Security security)
        {
            throw new NotImplementedException();
        }

        public void CancelOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public void ChangeOrderPrice(Order order, decimal newPrice)
        {
            throw new NotImplementedException();
        }

        public void GetAllActivOrders()
        {
            throw new NotImplementedException();
        }

        public List<Candle> GetCandleDataToSecurity(Security security, TimeFrameBuilder timeFrameBuilder, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            string period = timeFrameBuilder.TimeFrameTimeSpan.TotalSeconds.ToString();
            string securityId = security.NameId;

            if (startTime > actualTime)
            {
                startTime = actualTime;
            }

            string start = ((DateTimeOffset)startTime).ToUnixTimeSeconds().ToString();
            string end = ((DateTimeOffset)endTime).ToUnixTimeSeconds().ToString();

            return RequestCandlesHistory(securityId, period, start, end);
        }

        private List<Candle> RequestCandlesHistory(string security, string period, string start, string end)
        {
            try
            {
                Dictionary<string, string> parameters = new Dictionary<string, string>();
                parameters.Add("command", "returnChartData");
                parameters.Add("period", period);
                parameters.Add("currencyPair", security);
                parameters.Add("start", start);
                parameters.Add("end", end);

                SortedDictionary<string, string> sortedParameters = new SortedDictionary<string, string>(parameters);
                List<KeyValuePair<string, string>> sortedParametersList = sortedParameters.ToList();

                string query = string.Empty;
                for (int i = 0; i < sortedParametersList.Count; i++)
                {
                    query += $"{sortedParametersList[i].Key}={sortedParametersList[i].Value}";
                    query += "&";
                }
                query = query.Substring(0, query.Length - 1);

                HttpResponseMessage responseMessage = httpClient.GetAsync($"{host}/api/v1/public?{query}").Result;

                if (responseMessage.IsSuccessStatusCode)
                {
                    string responseContent = responseMessage.Content.ReadAsStringAsync().Result;
                    ResponseMessageRest<List<Dictionary<string, string>>> responseMessageRest = JsonConvert.DeserializeObject<ResponseMessageRest<List<Dictionary<string, string>>>>(responseContent);
                    if (responseMessageRest == null)
                    {
                        SendLogMessage("No candle from CoinW Spot. Can't parse list of candles", LogMessageType.Error);
                        return null;
                    }

                    if (responseMessageRest.code != 200.ToString())
                    {
                        SendLogMessage($"No candles from CoinW Spot. Code: {responseMessageRest.code}\n" +
                            $"Message: {responseMessageRest.msg}", LogMessageType.Error);
                        return null;
                    }

                    return GetCandles(responseMessageRest.data);
                }
                else
                {
                    SendLogMessage($"No candles from CoinW Spot. Code: {responseMessage.StatusCode}", LogMessageType.Error);
                    return null;
                }
            }
            catch (Exception ex)
            {
                SendLogMessage(ex.ToString(), LogMessageType.Error);
                return null;
            }
        }

        private List<Candle> GetCandles(List<Dictionary<string, string>> data)
        {
            List<Candle> candles = new List<Candle>();

            if (data == null || data.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < data.Count; i++)
            {
                Dictionary<string, string> dataCandle = data[i];

                Candle candle = new Candle();
                candle.State = CandleState.Finished;
                candle.TimeStart = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(dataCandle["date"])).UtcDateTime;
                candle.Volume = dataCandle["volume"].ToDecimal();
                candle.Open = dataCandle["open"].ToDecimal();
                candle.Close = dataCandle["close"].ToDecimal();
                candle.High = dataCandle["high"].ToDecimal();
                candle.Low = dataCandle["low"].ToDecimal();

                candles.Add(candle);
            }

            candles.Reverse();

            return candles;
        }

        public List<Candle> GetLastCandleHistory(Security security, TimeFrameBuilder timeFrameBuilder, int candleCount)
        {
            string period = timeFrameBuilder.TimeFrameTimeSpan.TotalSeconds.ToString();
            string securityId = security.NameId;

            int seconds = (int)(timeFrameBuilder.TimeFrameTimeSpan.TotalSeconds * candleCount * 1.3d);

            string start = ((DateTimeOffset)DateTime.UtcNow.AddSeconds(-seconds)).ToUnixTimeSeconds().ToString();
            string end = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds().ToString();

            List<Candle> candles = RequestCandlesHistory(securityId, period, start, end);

            while(candles.Count > candleCount)
            {
                candles.RemoveAt(0);
            }

            return candles;
        }

        public void GetOrderStatus(Order order)
        {
            throw new NotImplementedException();
        }

        public void GetPortfolios()
        {
            // AllBalances POST /api/v1/private?command=returnCompleteBalances
            try
            {
                Dictionary<string, string> parameters = new Dictionary<string, string>();
                parameters.Add("api_key", publicKey);
                SortedDictionary<string, string> sortedParameters = new SortedDictionary<string, string>(parameters);
                List<KeyValuePair<string, string>> sortedParametersList = sortedParameters.ToList();
                string line = string.Empty;
                for (int i = 0; i < sortedParametersList.Count; i++)
                {
                    line += $"{sortedParametersList[i].Key}={sortedParametersList[i].Value}";
                    line += "&";
                }

                line += $"secret_key={secretKey}";
                string signature = CreateMD5(line);
                parameters.Add("sign", signature);
                FormUrlEncodedContent content = new FormUrlEncodedContent(parameters);

                HttpResponseMessage responseMessage = httpClient.PostAsync($"{host}/api/v1/private?command=returnCompleteBalances", content).Result;
                if (responseMessage.IsSuccessStatusCode)
                {
                    string responseBody = responseMessage.Content.ReadAsStringAsync().Result;
                    ResponseMessageRest<Dictionary<string, AllBalances>> responseMessageRest = JsonConvert.DeserializeObject<ResponseMessageRest<Dictionary<string, AllBalances>>>(responseBody);
                    if (responseMessageRest == null)
                    {
                        SendLogMessage("No portfolios from CoinW Spot. Can't parse ResponseMessageRest with AllBalances", LogMessageType.Error);
                        return;
                    }

                    if (responseMessageRest.code != 200.ToString())
                    {
                        SendLogMessage($"No portfolios from CoinW Spot. Code: {responseMessageRest.code}\n" +
                            $"Message: {responseMessageRest.msg}", LogMessageType.Error);
                        return;
                    }

                    UpdatePortfolio(responseMessageRest.data);
                }
                else
                {
                    SendLogMessage($"No response message with Portfolios from CoinW Spot. Code: {responseMessage.StatusCode}", LogMessageType.Error);
                }

            }
            catch (Exception e)
            {
                SendLogMessage(e.ToString(), LogMessageType.Error);
            }
        }

        private void UpdatePortfolio(Dictionary<string, AllBalances> allBalances)
        {
            try
            {
                portfolio = new Portfolio();
                portfolio.Number = "CoinW_Spot";
                portfolio.ValueBegin = 1;
                portfolio.ValueCurrent = 1;
                /*
                List<KeyValuePair<string, AllBalances>> allBalancesList = allBalances.ToList();

                if (allBalances == null || allBalancesList.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < allBalancesList.Count; i++)
                {
                    PositionOnBoard positionOnBoard = new PositionOnBoard();
                    positionOnBoard.SecurityNameCode = allBalancesList[i].Key;
                    positionOnBoard.ValueBegin = allBalancesList[i].Value.available.ToDecimal();
                    positionOnBoard.ValueCurrent = allBalancesList[i].Value.available.ToDecimal();
                    positionOnBoard.ValueBlocked = allBalancesList[i].Value.onOrders.ToDecimal();
                    portfolio.SetNewPosition(positionOnBoard);
                }
                */
                PortfolioEvent(new List<Portfolio> { portfolio });
            }
            catch (Exception e)
            {
                SendLogMessage($"{e.Message} {e.StackTrace}", LogMessageType.Error);
            }
        }

        private string CreateMD5(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }

        public void GetSecurities()
        {
            try
            {
                HttpResponseMessage responseMessage = httpClient.GetAsync(host + "/api/v1/public?command=returnSymbol").Result;

                if (responseMessage.IsSuccessStatusCode)
                {
                    string responseContent = responseMessage.Content.ReadAsStringAsync().Result;
                    ResponseMessageRest<List<TradingPair>> responseMessageRest = JsonConvert.DeserializeObject<ResponseMessageRest<List<TradingPair>>>(responseContent);
                    if (responseMessageRest == null)
                    {
                        SendLogMessage("No securities from CoinW Spot. Can't parse list of Trading Pairs", LogMessageType.Error);
                        return;
                    }

                    if (responseMessageRest.code != 200.ToString())
                    {
                        SendLogMessage($"No securities from CoinW Spot. Code: {responseMessageRest.code}\n" +
                            $"Message: {responseMessageRest.msg}", LogMessageType.Error);
                        return;
                    }

                    UpdateSecurity(responseMessageRest.data);
                }
                else
                {
                    SendLogMessage($"No securities from CoinW Spot. Code: {responseMessage.StatusCode}", LogMessageType.Error);
                }
            }
            catch (Exception e)
            {
                SendLogMessage(e.ToString(), LogMessageType.Error);
            }
        }

        private void UpdateSecurity(List<TradingPair> tradingPairs)
        {
            List<Security> securities = new List<Security>();

            for (int i = 0; i < tradingPairs.Count; i++)
            {
                TradingPair tradingPair = tradingPairs[i];

                Security security = new Security();
                security.Name = tradingPair.currencyPair;
                security.NameFull = tradingPair.currencyPair;
                security.NameClass = tradingPair.currencyQuote;
                security.NameId = tradingPair.currencyPair;
                security.Exchange = ServerType.CoinWSpot.ToString();
                security.State = SecurityStateType.Activ;
                security.Decimals = Convert.ToInt32(tradingPair.pricePrecision);
                security.DecimalsVolume = Convert.ToInt32(tradingPair.countPrecision);
                security.PriceStep = security.Decimals.GetValueByDecimals();
                security.Lot = security.DecimalsVolume.GetValueByDecimals();
                security.PriceStepCost = security.PriceStep;
                security.SecurityType = SecurityType.CurrencyPair;

                securities.Add(security);
            }

            SecurityEvent(securities);
        }

        public List<Trade> GetTickDataToSecurity(Security security, DateTime startTime, DateTime endTime, DateTime actualTime)
        {
            return null;
        }

        public void SendOrder(Order order)
        {
            throw new NotImplementedException();
        }

        public void Subscrible(Security security)
        {
            try
            {
                if (!subscribedSecurities.ContainsKey(security.NameId))
                {
                    subscribedSecurities.Add(security.NameId, security);
                }

                // Для подписки на сокеты используется симвом с тире а не нижним подчеркиванием!
                string symbol = security.NameId.Replace('_', '-');

                SubscribeOrderBook(symbol);
                SubscribeTransactionHistory(symbol);
            }
            catch (Exception ex)
            {
                SendLogMessage(ex.ToString(), LogMessageType.Error);
            }
        }

        private void SubscribeTransactionHistory(string symbol)
        {
            string subscribeMessage = $"42[\"subscribe\",{{\"args\":\"spot/match:{symbol}\"}}]";

            lock (socketLocker)
            {
                webSocket.Send(subscribeMessage);
            }
        }

        private void SubscribeOrderBook(string symbol)
        {
            string subscribeMessage = $"42[\"subscribe\",{{\"args\":\"spot/level2:{symbol}\"}}]";

            lock (socketLocker)
            {
                //webSocket.Send(subscribeMessage);
            }
        }

        #region Log

        public event Action<string, LogMessageType> LogMessageEvent;

        private void SendLogMessage(string message, LogMessageType messageType)
        {
            LogMessageEvent(message, messageType);
        }

        #endregion
    }
}