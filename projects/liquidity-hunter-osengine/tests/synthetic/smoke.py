#!/usr/bin/env python3
"""Portable deterministic synthetic L2 regression for OS Engine strategy concepts.
No OS Engine dependency, no live broker, NOT a truthful reconstruction of exchange queues.
python3 tests/synthetic/smoke.py
"""
import math, random, statistics
SCENARIOS=("range","trend_down","trend_up","flash_crash","liquidity_vanish","false_wall",
 "wide_spread","tight_spread","volatility_shock","halt","reconnect","correlated_crash")
def generate(kind,seed=14,n=480):
 r=random.Random(seed);p=100.;out=[]
 for t in range(n):
  drift=-.05 if kind=="trend_down" else .05 if kind=="trend_up" else 0.
  shock=(-.7 if kind=="flash_crash" and 80<=t<95 else 0.)
  if kind=="correlated_crash" and 200<=t<240:shock=-.27
  vol=.7 if kind=="volatility_shock" and 250<=t<310 else .09
  p=max(1.,p+drift+shock+r.gauss(0,vol))
  spread=.06 if kind=="tight_spread" else .70 if kind=="wide_spread" else .25
  depth=60. if kind!="liquidity_vanish" or not 80<=t<100 else .2
  bd=depth*r.uniform(.4,1.6);ad=depth*r.uniform(.4,1.6)
  if kind=="false_wall" and 140<=t<180:ad=5000 if t%2 else .3
  out.append((round(p-spread/2,3),round(p+spread/2,3),bd,ad,
              kind=="halt" and 170<=t<190,kind=="reconnect" and 170<=t<190))
 return out
def simulate(data,improved,fee_bps=8):
 pending=None;holding=None;history=[];depths=[];pnl=0.;fills=0;loss=0
 for t,(bid,ask,bd,ad,halt,stale) in enumerate(data):
  mid=(bid+ask)/2
  prices=history[-20:];old_depths=depths[-8:]
  dispersion=statistics.pstdev([prices[i]-prices[i-1] for i in range(1,len(prices))]) if len(prices)>4 else 0.
  stability=min(old_depths)/max(old_depths) if old_depths and min(old_depths)>0 else 0.
  history.append(mid);depths.append(bd+ad)
  if halt or stale:continue
  if holding:
   entry,opened=holding
   if bid>=entry+.35 or bid<=entry-.85 or t-opened>=50:
    cost=(entry+bid)*fee_bps/20000.
    result=bid-entry-cost
    pnl+=result;loss+=result<0;fills+=1;holding=None
   continue
  if pending is not None:
   price,placed=pending
   if bid<price-.005:holding=(price,t);pending=None
   elif t-placed>=12:pending=None
   continue
  if t<25:continue
  spread=10000*(ask-bid)/mid
  imbalance=(bd-ad)/(bd+ad)
  if spread<15 or imbalance>-.1:continue
  if improved:
   if .35<=mid*fee_bps/10000.:continue
   if dispersion>.18 or stability<.30 or bd+ad<20:continue
   if len(prices)>8 and mid-prices[-8]<-.20:continue
   if old_depths and max(old_depths)/max(.01,min(old_depths))>25:continue
  pending=(bid,t)
 return round(pnl,4),fills,loss
def run():
 cases=0;out=[]
 for kind in SCENARIOS:
  base=improved=0.;bcount=icount=0
  for symbol in range(20):
   data=generate(kind,seed=1000+symbol)
   for row in data:
    assert row[0]<row[1] and row[2]>0 and row[3]>0
   x,xc,_=simulate(data,False);y,yc,_=simulate(data,True)
   base+=x;improved+=y;bcount+=xc;icount+=yc;cases+=2
  out.append((kind,round(base,2),round(improved,2),bcount,icount))
 assert cases==len(SCENARIOS)*20*2
 assert all(math.isfinite(a) and math.isfinite(b) for _,a,b,_,_ in out)
 for kind,a,b,ac,bc in out:print(f"{kind:20} base={a:+9.2f} filtered={b:+9.2f} trades={ac}/{bc}")
 print(f"PASS: {cases} scenario-symbol-policy tests; results are synthetic, not evidence of alpha")
if __name__=="__main__":run()
