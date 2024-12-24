namespace Content.Server.Atmos.GasReactionHelpers
{
	public static class GasReactionHelperFunctions{
	
	

		/*
		this is to simulate an under/oversaturated mixture. closer to the ideal mix means more reaction.
		do NOT set the idealratio to 1.0 or 0.0, as that may cause a div 0 - so do that at your own risk.
		0.0 <-> 1.0 ; 0.0 = all gas2, 1.0 = all gas 1, 0.5 = same of both. you get the idea.
		here's the function in math format, you can paste it into desmos and move the var around to see how it works.
			\max\left(0,\left\{x>R_{ideal}:\frac{\left(x-1\right)\left(-x+2R_{ideal}-1\right)}{\left(1-R_{ideal}\right)^{2}},\frac{\left(x\right)\left(2R_{ideal}-x\right)}{R_{ideal}^{2}}\right\}\right)
		*/
		public static float reaction_mult_2gas_idealratio(float idealratio, float molesgas1,float molesgas2){
			if(molesgas1<=0.0f || molesgas2 <=0.0f){
				return 0.0f;
			}
			float reactionmultiplier=0.0f;
			float currentratio=molesgas1/(molesgas1+molesgas2);
		
			//this function uses a fancy parabolic interpolation. results in less effect harshness.
			if (currentratio>idealratio){
				reactionmultiplier= (currentratio-1)*(-currentratio+2f*idealratio-1f)/((1f-idealratio)*(1f-idealratio));
			}else{
				reactionmultiplier= (idealratio)*(2f*idealratio-currentratio)/(idealratio*idealratio);
			}
		
			reactionmultiplier=MathF.Max(0.0f,reactionmultiplier); //clamp to 0.0 to 1.0. we don't have to call min because that's already done for us in the previous if check.
		
			return reactionmultiplier;
		}
	
	/*
		this was used before, however the linear results act almost as a band-pass and result in undesirable behavior (really slow rates) when off-ratio. still here if you want to use it for whatever reason
		formula:
			\max\left(0,\left\{x>R_{ideal}:\frac{\left(1-x\right)}{1-R_{ideal}},\frac{x}{R_{ideal}}\right\}\right)
		*/
		public static float reaction_mult_2gas_idealratio_linear(float idealratio, float molesgas1,float molesgas2){
			if(molesgas1<=0.0f || molesgas2 <=0.0f){
				return 0.0f;
			}
			float reactionmultiplier=0.0f;
			float currentratio=molesgas1/(molesgas1+molesgas2);
		
			//this function is essentially a linear interpolation between the point (0,0) -> (idealratio,1) and (idealratio,1) -> (1,0)
			if (currentratio>idealratio){
				reactionmultiplier=(1.0f-currentratio)/(1.0f-idealratio);
			}else{
				reactionmultiplier= currentratio/idealratio;
			}
		
			reactionmultiplier=MathF.Max(0.0f,reactionmultiplier); //clamp to 0.0 to 1.0. we don't have to call min because that's already done for us in the previous if check.
		
			return reactionmultiplier;
		}
	
		
		/*
		this function makes higher temps increase.
		function characteristics: x-> +inf, y-> 1 ; x->[mintemp], y->0
		any temp below mintemp returns 0. above this, it increases with diminishing returns. the variable temphalfpoint determines the steepness. when the temp beyond the minimum temp is equal to this value, the output will be 0.5
		temphalfpoint should be above 0.
		math format function:
			1-\frac{t_{half}}{\max\left(0.0,x-t_{min}\right)+t_{half}}
		*/
		public static float reaction_mult_mintemp_higher_asymptote(float actualtemp, float mintemp,float temphalfpoint){
			float tf= 1.0f-  temphalfpoint/(MathF.Max(0.0f,actualtemp-mintemp)+temphalfpoint );
			return tf*tf; //square the result to make it a bit less abrupt.
		}
		
		/*
		essentially the same as above, except the direction is reversed.
		math format function:
		1+\frac{t_{half}}{\min\left(0.0,x-t_{min}\right)-t_{half}}
		*/
		public static float reaction_mult_maxtemp_lower_asymptote(float actualtemp, float mintemp,float temphalfpoint){
			float tf= 1.0f+  temphalfpoint/(MathF.Min(0.0f,actualtemp-mintemp)-temphalfpoint );
			return tf*tf;
		}
	
		/*
			gives a finite band in which the value is above 0. the value peaks at peaktemp. this doesn't use linear interpolation, but rather creates a piecewise parabola to make a silky smooth curve.
			it's a bit more complex, though.
			peaktemp should be between mintemp and maxtemp, and maxtemp should be greater than mintemp. duh.
			math formula:
				\max\left(0,\left\{x>m:1-\frac{\left(x-m\right)}{\left(b-m\right)}^{2},1-\frac{\left(x-m\right)}{\left(m-a\right)}^{2}\right\}\right)	
		*/
		public static float reaction_mult_finite_band(float actualtemp, float mintemp,float peaktemp,float maxtemp){
			float reactionmultiplier=1.0f;
			float tempval = (actualtemp-peaktemp);
			if(actualtemp>peaktemp){
				tempval/=maxtemp-peaktemp;
				reactionmultiplier-= tempval*tempval;
			}else{
				tempval/=peaktemp-mintemp;
				reactionmultiplier-= tempval*tempval;
			}
			
			reactionmultiplier=MathF.Max(0.0f,reactionmultiplier);
			return reactionmultiplier;
		}

	}
}