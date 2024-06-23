using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsEngine.Market.Servers.CoinW.CoinWSpot.Json
{
    public class ResponseMessageRest<T>
    {
        public string code;
        public T data;
        public string failed;
        public string msg;
        public string success;
    }

    public class WebSocketInformation
    {
        public string token;
        public string endpoint;
        public string protocol;
        public string timestamp;
        public string expiredTime;
        public string pingInterval;
    }

    public class AllBalances
    {
        public string available;
        public string onOrders;
    }
    public class TradingPairInformation
    {
        public string id;
        public string last;
        public string lowestAsk;
        public string highestBid;
        public string percentChange;
        public string isFrozen;
        public string high24hr;
        public string low24hr;
        public string baseVolume;
    }

    public class Coin
    {
        public string chain;
        public string maxQty;
        public string minQty;
        public string recharge;
        public string symbol;
        public string symbolId;
        public string txFee;
        public string withDraw;
    }

    public class TradingPair
    {
        public string currencyPair;
        public string currencyBase;
        public string currencyQuote;
        public string maxBuyCount;
        public string minBuyCount;
        public string pricePrecision;
        public string countPrecision;
        public string minBuyAmount;
        public string maxBuyAmount;
        public string minBuyPrice;
        public string maxBuyPrice;
        public string state;
    }
}