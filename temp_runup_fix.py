from pathlib import Path
path = Path(r'MT5/ElasticHedgeMaster.mq5')
text = path.read_text(encoding='utf-8', errors='replace')
old = """    bool sent = (request.type == ORDER_TYPE_BUY)
                ? trade.Buy (finalVol, _Symbol, request.price,
                             slPrice, tpPrice, request.comment)
                : trade.Sell(finalVol, _Symbol, request.price,
                             slPrice, tpPrice, request.comment);

    if(!sent)
    {
        int lastErr = GetLastError();
        int retcode = (int)trade.ResultRetcode();
        string retmsg = trade.ResultComment();
        // Unified logging on failure with error context
        ULogSetErrorCode(IntegerToString(retcode) + "|" + IntegerToString(lastErr));
        ULogSetMt5Ticket(0);
        ULogErrorPrint(StringFormat(
            "HEDGE_ORDER_FAILED: base_id=%s action=%s vol=%.4f type=%s retcode=%d lastErr=%d comment='%s'",
            tradeId, hedgeOrigin, finalVol, EnumToString(request.type), retcode, lastErr, retmsg
        ));

        PrintFormat("ERROR  CTrade %s failed (%d / %s)",
                    (request.type == ORDER_TYPE_BUY ? "Buy" : "Sell"),
                    retcode, retmsg);
        // Submit failure so bridge can correlate
        SubmitTradeResult("failed", 0, finalVol, false, tradeId);
        return false;
    }
"""
new = """    bool sent = (request.type == ORDER_TYPE_BUY)
                ? trade.Buy(finalVol, _Symbol, request.price, slPrice, tpPrice, request.comment)
                : trade.Sell(finalVol, _Symbol, request.price, slPrice, tpPrice, request.comment);

    int sendRetcode = (int)trade.ResultRetcode();
    string sendComment = trade.ResultComment();
    if(!sent)
    {
        int lastErr = GetLastError();
        ULogSetErrorCode(IntegerToString(sendRetcode) + "|" + IntegerToString(lastErr));
        ULogSetMt5Ticket(0);
        ULogErrorPrint(StringFormat(
            "HEDGE_ORDER_FAILED: base_id=%s action=%s vol=%.4f type=%s retcode=%d lastErr=%d comment='%s'",
            tradeId, hedgeOrigin, finalVol, EnumToString(request.type), sendRetcode, lastErr, sendComment
        ));

        PrintFormat("ERROR: CTrade %s failed (%d / %s)",
                    (request.type == ORDER_TYPE_BUY ? "Buy" : "Sell"),
                    sendRetcode, sendComment);
        SubmitTradeResult("failed", 0, finalVol, false, tradeId);
        return false;
    }
    else
    {
        { string __log=""; StringConcatenate(__log,
            "HEDGE_ORDER_SENT: base_id=", tradeId,
            " action=", hedgeOrigin,
            " vol=", DoubleToString(finalVol, 4),
            " sl=", DoubleToString(slPrice, _Digits),
            " tp=", DoubleToString(tpPrice, _Digits),
            " retcode=", sendRetcode,
            " comment=", sendComment);
          Print(__log); ULogInfoPrint(__log); }
    }
"""
if old not in text:
    raise SystemExit('old block not found')
path.write_text(text.replace(old, new), encoding='utf-8')
