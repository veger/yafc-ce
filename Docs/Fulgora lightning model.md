This is the math used to calculate `requiredChargeMw` (called "_cap_" here) in `BuildAccumulatorView`.
The first two equations are the starting point, and the remainder are solving for _cap_.

_chargeTime_ is per lightning strike.
_eff_ is the efficiency of the lightning attractor.

$$chargeTime=\frac{1000MJ\times eff}{drain+cap+load}$$
$$cap\times chargeTime\times numStrikes-load\times(stormTime-chargeTime\times numStrikes)=reqMj$$
$$\frac{cap\times 1000MJ\times eff\times numStrikes}{drain+cap+load}-load\times\left(stormTime-\frac{1000MJ\times eff\times numStrikes}{drain+cap+load}\right)=reqMj$$
$$\frac{cap\times 1000MJ\times eff\times numStrikes}{drain+cap+load}-load\times stormTime+\left(\frac{load\times 1000MJ\times eff\times numStrikes}{drain+cap+load}\right)=reqMj$$
$$\frac{cap\times 1000MJ\times eff\times numStrikes-load\times stormTime\times(drain+cap+load)+load\times 1000MJ\times eff\times numStrikes}{drain+cap+load}=reqMj$$
$$cap\times 1000MJ\times eff\times numStrikes-load\times stormTime\times(drain+cap+load)+load\times 1000MJ\times eff\times numStrikes\\
=reqMj\times(drain+cap+load)$$
$$cap\times 1000MJ\times eff\times numStrikes-load\times stormTime\times drain-load\times stormTime\times cap\\
-\ load\times stormTime\times load+load\times 1000MJ\times eff\times numStrikes\\
=reqMj\times drain+reqMj\times cap+reqMj\times load$$
$$cap\times 1000MJ\times eff\times numStrikes-load\times stormTime\times cap-reqMj\times cap\\
\begin{aligned}
=\ &reqMj\times drain+reqMj\times load+load\times stormTime\times drain\\
&+load\times stormTime\times load-load\times 1000MJ\times eff\times numStrikes
\end{aligned}$$
$$\begin{aligned}
cap\times(&1000MJ\times eff\times numStrikes-load\times stormTime-reqMj)\\
&=reqMj\times drain+load\times(reqMj+stormTime\times drain+stormTime\times load-1000MJ\times eff\times numStrikes)
\end{aligned}$$
$$cap=\frac{reqMj\times drain+load\times(reqMj+stormTime\times(drain+load)-1000MJ\times eff\times numStrikes)}{1000MJ\times eff\times numStrikes-load\times stormTime-reqMj}$$
