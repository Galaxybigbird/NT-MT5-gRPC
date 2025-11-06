export namespace main {
	
	export class Trade {
	    id: string;
	    base_id: string;
	    // Go type: time
	    time: any;
	    action: string;
	    quantity: number;
	    price: number;
	    total_quantity: number;
	    contract_num: number;
	    order_type: string;
	    measurement_pips: number;
	    raw_measurement: number;
	    instrument: string;
	    account_name: string;
	    nt_balance?: number;
	    nt_daily_pnl?: number;
	    nt_trade_result?: string;
	    nt_session_trades?: number;
	    mt5_ticket: number;
	    nt_points_per_1k_loss?: number;
	    event_type?: string;
	    elastic_current_profit?: number;
	    elastic_profit_level?: number;
	    lifecycle_event?: string;
	    remaining_quantity?: number;
	    seq?: number;
	    epoch?: number;
	    direction?: string;
	    target_lots?: number;
	
	    static createFrom(source: any = {}) {
	        return new Trade(source);
	    }
	
	    constructor(source: any = {}) {
	        if ('string' === typeof source) source = JSON.parse(source);
	        this.id = source["id"];
	        this.base_id = source["base_id"];
	        this.time = this.convertValues(source["time"], null);
	        this.action = source["action"];
	        this.quantity = source["quantity"];
	        this.price = source["price"];
	        this.total_quantity = source["total_quantity"];
	        this.contract_num = source["contract_num"];
	        this.order_type = source["order_type"];
	        this.measurement_pips = source["measurement_pips"];
	        this.raw_measurement = source["raw_measurement"];
	        this.instrument = source["instrument"];
	        this.account_name = source["account_name"];
	        this.nt_balance = source["nt_balance"];
	        this.nt_daily_pnl = source["nt_daily_pnl"];
	        this.nt_trade_result = source["nt_trade_result"];
	        this.nt_session_trades = source["nt_session_trades"];
	        this.mt5_ticket = source["mt5_ticket"];
	        this.nt_points_per_1k_loss = source["nt_points_per_1k_loss"];
	        this.event_type = source["event_type"];
	        this.elastic_current_profit = source["elastic_current_profit"];
	        this.elastic_profit_level = source["elastic_profit_level"];
	        this.lifecycle_event = source["lifecycle_event"];
	        this.remaining_quantity = source["remaining_quantity"];
	        this.seq = source["seq"];
	        this.epoch = source["epoch"];
	        this.direction = source["direction"];
	        this.target_lots = source["target_lots"];
	    }
	
		convertValues(a: any, classs: any, asMap: boolean = false): any {
		    if (!a) {
		        return a;
		    }
		    if (a.slice && a.map) {
		        return (a as any[]).map(elem => this.convertValues(elem, classs));
		    } else if ("object" === typeof a) {
		        if (asMap) {
		            for (const key of Object.keys(a)) {
		                a[key] = new classs(a[key]);
		            }
		            return a;
		        }
		        return new classs(a);
		    }
		    return a;
		}
	}

}

