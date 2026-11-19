"""Deterministic multi-asset eventlab stress; no real market data or broker orders.
Run: python stress.py --seed 20261120 --symbols 12 --ticks 240
Results are *synthetic cents*, not PnL forecast.
"""
import argparse,json,random
from engine import Engine,Policy,BookEvent

SCENARIOS=("range","downtrend","uptrend","flash_crash","thin_book","false_wall",
           "wide_spread","tight_spread","high_vol","halt","stale","correlated_crash")
def run(seed,symbols,ticks,filtered,fee_bps):
    p=Policy(fee_bps=fee_bps)
    out=[]
    for regime in SCENARIOS:
        e=Engine(p);rng=random.Random(seed+101*SCENARIOS.index(regime))
        prices=[9000+200*i for i in range(symbols)]
        last_mid=[None]*symbols
        for t in range(ticks):
            market=rng.gauss(0,5)
            for i in range(symbols):
                now=t*symbols+i
                drift=-4 if regime=="downtrend" else 4 if regime=="uptrend" else 0
                jump=-70 if regime=="flash_crash" and 70<=t<80 else 0
                if regime=="correlated_crash" and 110<=t<130:jump=-24
                sigma=50 if regime=="high_vol" and 100<=t<160 else 8
                prices[i]=max(100,round(prices[i]+drift+jump+market+rng.gauss(0,sigma)))
                spread=7 if regime=="tight_spread" else 75 if regime=="wide_spread" else 25
                bid=prices[i]-spread//2;ask=bid+spread
                qty=1 if regime=="thin_book" and 70<=t<90 else rng.randint(20,90)
                askqty=(3000 if t%2 else 1) if regime=="false_wall" and 90<=t<110 else rng.randint(20,90)
                halted=regime=="halt" and 120<=t<140
                stale=regime=="stale" and 120<=t<140
                sold=rng.randrange(40) if drift+jump<0 else rng.randrange(16)
                event=BookEvent(now,f"X{i}",bid,ask,qty,askqty,sold,halted,stale)
                e.event(event)
                previous=last_mid[i];last_mid[i]=(bid+ask)//2
                if t<25 or halted or stale or (qty-askqty)/(qty+askqty)>-.10 or spread<15:continue
                if filtered and (qty+askqty<25 or (previous is not None and abs(last_mid[i]-previous)>30)
                    or spread*10000//max(1,last_mid[i]) < fee_bps):continue
                e.place(f"X{i}",bid,3,now,qty//2)
        e.finish(ticks*symbols+20)
        assert e.committed()==0
        assert e.stats.max_committed<=p.max_budget and e.stats.max_positions<=p.max_positions
        out.append({"regime":regime,"pnl_cents":round(e.stats.realized_pnl,3),
                    "shares_filled":e.stats.entry_fills,"cancel_acks":e.stats.canceled,
                    "max_committed":e.stats.max_committed})
    return {"seed":seed,"policy":"filtered" if filtered else "baseline","fee_bps":fee_bps,
            "pnl_cents":round(sum(x["pnl_cents"] for x in out),2),
            "shares_filled":sum(x["shares_filled"] for x in out),"regimes":out}
if __name__=="__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--seed",type=int,default=20261120)
    parser.add_argument("--symbols",type=int,default=12)
    parser.add_argument("--ticks",type=int,default=240)
    parser.add_argument("--out",default="stress-output.json")
    a=parser.parse_args()
    results=[run(a.seed,a.symbols,a.ticks,filtered,fee) for fee in (0,8,16) for filtered in (False,True)]
    with open(a.out,"w",encoding="utf8") as f:json.dump({"runs":results},f,indent=2)
    for r in results:print(r["fee_bps"],r["policy"],r["pnl_cents"],r["shares_filled"])
    print("PASS: 12 synthetic regimes, cash and slot invariants")
