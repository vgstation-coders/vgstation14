using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class FrezonPlasmaReaction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			const float energy_per_mole = 1000e3f; //900 kJ/mol. endothermic.
			const float minimum_temp = 0f; // 0K. ice cold.
			const float halfstep_temp = Atmospherics.T0C; //0C
			
			float currenttemp=mixture.Temperature;
			float totalsystemenergy= atmosphereSystem.GetHeatCapacity(mixture, true)*currenttemp;
			float plas = mixture.GetMoles(Gas.Plasma);
			float frez = mixture.GetMoles(Gas.Frezon);
			float conversionratio=1.0f; 
			
			if(plas<=0f || frez<=0f){
				return ReactionResult.NoReaction;
			}
			
			conversionratio*=GasReactionHelperFunctions.reaction_mult_2gas_idealratio(.5f,plas,frez);
			conversionratio*=GasReactionHelperFunctions.reaction_mult_mintemp_higher_asymptote(currenttemp, minimum_temp ,halfstep_temp );
			conversionratio*=(frez+plas)/(mixture.TotalMoles);
			
			if (conversionratio>0f){
				float molestoreact=MathF.Min(plas,frez)*conversionratio;
				
				totalsystemenergy-=molestoreact*energy_per_mole/heatScale;
				
				mixture.AdjustMoles(Gas.Frezon, -molestoreact);
				
				float newheatcap=atmosphereSystem.GetHeatCapacity(mixture, true); //add new heat
				if (newheatcap > Atmospherics.MinimumHeatCapacity){
					mixture.Temperature=totalsystemenergy/newheatcap;
				}

				return ReactionResult.Reacting;
			}
			return ReactionResult.NoReaction;



		
		}
	}
}