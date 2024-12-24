using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class FrezonOxygenReaction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			const float energy_per_mole = 250e3f; //250 kJ/mol. endothermic. this has to be high because we are deleting moles which messes with the total energy quite a bit.
			const float minimum_temp = Atmospherics.T0C-30f; // -40C
			const float halfstep_temp = 80f;
			
			float currenttemp=mixture.Temperature;
			float totalsystemenergy= atmosphereSystem.GetHeatCapacity(mixture, true)*currenttemp;
			float oxy = mixture.GetMoles(Gas.Oxygen);
			float frez = mixture.GetMoles(Gas.Frezon);
			float conversionratio=1.0f; 
			
			
			
			if(oxy<=0f || frez<=0f){
				return ReactionResult.NoReaction;
			}
			
			conversionratio*=GasReactionHelperFunctions.reaction_mult_mintemp_higher_asymptote(currenttemp, minimum_temp ,halfstep_temp );
			conversionratio*=(frez+oxy)/(mixture.TotalMoles);
			
			if (conversionratio>0f){
				float molestoreact=MathF.Min(oxy,frez)*conversionratio;
				
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