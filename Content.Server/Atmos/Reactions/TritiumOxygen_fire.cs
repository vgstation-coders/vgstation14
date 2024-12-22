using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class TritiumOxygenFire : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			mixture.ReactionResults[GasReaction.Fire]=0f;
			// 2(H2) + O2 -> 2(H2O)
			const float energy_per_mole = 482e3f; //482 kJ/mol. this will be halved later because 2 mols in the H2 to H2O
			const float minimum_temp = Atmospherics.T0C+225f; // 200C. this number is pure make-believe.
			const float halfstep_temp = 100f; // 500C. what temp past min it will be at half speed.
			
			float currenttemp=mixture.Temperature;
			float totalsystemenergy= atmosphereSystem.GetHeatCapacity(mixture, true)*currenttemp;
			float oxy = mixture.GetMoles(Gas.Oxygen);
			float trit = mixture.GetMoles(Gas.Tritium);
			float stm = mixture.GetMoles(Gas.Tritium);
			float conversionratio=1.0f; 
			
			if (oxy<=0.0f || trit<=0.0f){
				return ReactionResult.NoReaction;
			}
			
			conversionratio*=GasReactionHelperFunctions.reaction_mult_2gas_idealratio(.33333333333f,oxy,trit); // 1:2
			conversionratio*=GasReactionHelperFunctions.reaction_mult_mintemp_higher_asymptote(currenttemp, minimum_temp ,halfstep_temp );
			conversionratio*=(oxy+trit)/(mixture.TotalMoles-stm);
			
			if(conversionratio>0.0f){
				float molesconv=MathF.Min(oxy*.5f,trit)*conversionratio;
				
				totalsystemenergy+= energy_per_mole*.5f*molesconv/heatScale; //half it because that is for a complete reaction, whereas we just use half moles.
				
				mixture.AdjustMoles(Gas.Tritium,  -molesconv  );
				mixture.AdjustMoles(Gas.Oxygen,  -molesconv*.5f  );
				mixture.AdjustMoles(Gas.WaterVapor,  molesconv  );
				
				float newheatcap=atmosphereSystem.GetHeatCapacity(mixture, true); //add new heat
				if (newheatcap > Atmospherics.MinimumHeatCapacity){
					mixture.Temperature=totalsystemenergy/newheatcap;
				}
				
				if (molesconv>.01f){
					mixture.ReactionResults[GasReaction.Fire] += molesconv;
					var location = holder as TileAtmosphere; //start fires
					if (location!=null && mixture.Temperature > Atmospherics.FireMinimumTemperatureToExist){
						atmosphereSystem.HotspotExpose(location, mixture.Temperature, mixture.Volume);
					}
				}
				return ReactionResult.Reacting;
			}
			return ReactionResult.NoReaction;
			
			
			
		}
	}
}