using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class AmoniaOxygen_fire : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			mixture.ReactionResults[GasReaction.Fire]=0f;
			// 2(O2) + 2(NH3) -> N2O + 3(H2O)
			const float energy_per_mole = 46e3f; //46 kJ/mol.
			const float minimum_temp = Atmospherics.T0C+250f; // 275C. this number is pure make-believe.
			const float halfstep_temp = 150f; // 150C. what temp past min it will be at half speed.
			
			float currenttemp=mixture.Temperature;
			float totalsystemenergy= atmosphereSystem.GetHeatCapacity(mixture, true)*currenttemp;
			float oxy = mixture.GetMoles(Gas.Oxygen);
			float amonia = mixture.GetMoles(Gas.Ammonia);
			float nitrox = mixture.GetMoles(Gas.NitrousOxide);
			float stm = mixture.GetMoles(Gas.NitrousOxide);
			float conversionratio=1.0f; 
			
			if(oxy<=0.0f || amonia<=0.0f){
				return ReactionResult.NoReaction;
			}
			
			conversionratio*=GasReactionHelperFunctions.reaction_mult_2gas_idealratio(.5f,oxy,amonia); // equal parts.
			conversionratio*=GasReactionHelperFunctions.reaction_mult_mintemp_higher_asymptote(currenttemp, minimum_temp ,halfstep_temp );
			conversionratio*=(oxy+amonia)/(mixture.TotalMoles-(nitrox+stm));
			

			
			if (conversionratio>0.0f){
				float molesconv=MathF.Min(oxy,amonia)*conversionratio;
				
				totalsystemenergy+= energy_per_mole*.5f*molesconv/heatScale; //half it because that is for a complete reaction, whereas we just use half moles.
				
				mixture.AdjustMoles(Gas.Ammonia,  -molesconv  );
				mixture.AdjustMoles(Gas.Oxygen,  -molesconv  );
				mixture.AdjustMoles(Gas.NitrousOxide,  molesconv*.5f  );
				mixture.AdjustMoles(Gas.WaterVapor,  molesconv*1.5f  );
				
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