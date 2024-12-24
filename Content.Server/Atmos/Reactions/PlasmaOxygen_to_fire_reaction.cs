using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class PlasmaOxygen_Reaction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			mixture.ReactionResults[GasReaction.Fire]=0f;
			
			const float plasmafire_energy_per_mole = 2000e3f; //2 MJ/mol. blasma.
			const float plasmafire_minimum_temp = Atmospherics.T0C+200f; // 250C. below this it will not react.
			const float plasmafire_halfstep_temp = 1750f; // 1750C. what temp past min it will be at half speed.
			const float plasmafire_oxygenratio = .4f; //40-60 ratio of oxygen to plasma.

			float currenttemp=mixture.Temperature;
			float totalsystemenergy= atmosphereSystem.GetHeatCapacity(mixture, true)*currenttemp;
			float heatgenerated=0.0f;
			float oxy = mixture.GetMoles(Gas.Oxygen);
			float plas = mixture.GetMoles(Gas.Plasma);
			float carb = mixture.GetMoles(Gas.CarbonDioxide);
			float conversionratio=1.0f; //multiply it by the factors to get the final.
			
			conversionratio*=GasReactionHelperFunctions.reaction_mult_2gas_idealratio(plasmafire_oxygenratio,oxy,plas); // 40-60 ratio
			conversionratio*=GasReactionHelperFunctions.reaction_mult_mintemp_higher_asymptote(currenttemp, plasmafire_minimum_temp ,plasmafire_halfstep_temp );
			conversionratio*=(oxy+plas)/(mixture.TotalMoles-carb); //adjust by total amount of gas that is not this. do not penalize waste products.
			
			if (conversionratio>0.0f){ //if we are burning
				//this ensures that the output will be clamped to 0-1 for any ratio.
				float oxyfactor=plasmafire_oxygenratio/MathF.Max(plasmafire_oxygenratio,1.0f-plasmafire_oxygenratio);
				float plasmafactor=(1.0f-plasmafire_oxygenratio)/MathF.Max(plasmafire_oxygenratio,1.0f-plasmafire_oxygenratio);
				
				
				
				totalsystemenergy+=plasmafire_energy_per_mole*plas*plasmafactor*conversionratio/heatScale;
				
				mixture.AdjustMoles(Gas.Plasma,  -plas*(conversionratio)*plasmafactor  );
				mixture.AdjustMoles(Gas.Oxygen,  -oxy*(conversionratio)*oxyfactor  );
				
				float pmc=plas*(conversionratio)*plasmafactor;
				float omc=oxy*(conversionratio) *oxyfactor;
				
				mixture.AdjustMoles(Gas.CarbonDioxide,  plas*(conversionratio)*plasmafactor  );
				
				float newheatcap=atmosphereSystem.GetHeatCapacity(mixture, true); //add nwe heat
				if (newheatcap > Atmospherics.MinimumHeatCapacity){
					mixture.Temperature=totalsystemenergy/newheatcap;
				}
				
				if (plas*conversionratio*plasmafactor + oxy*conversionratio*oxyfactor >.01f){
					mixture.ReactionResults[GasReaction.Fire]+=plas*conversionratio*plasmafactor + oxy*conversionratio*oxyfactor;
					var location = holder as TileAtmosphere; //start fires
					if (location!=null && mixture.Temperature > Atmospherics.FireMinimumTemperatureToExist){
						atmosphereSystem.HotspotExpose(location, mixture.Temperature, mixture.Volume);
					}
				}
				
				return ReactionResult.Reacting;
			}
			//if we are not...
			return ReactionResult.NoReaction; //just end calcs.
		}	
	}
}