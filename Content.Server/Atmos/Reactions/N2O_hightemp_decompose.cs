using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class N2O_decompose_Reaction : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			//source: http://vias.org/genchem/standard_enthalpies_table.html
			const float energy_per_mole_decomposed = 81.55e3f; //81.55 kJ/mole. 
			const float min_temp_to_decompose = Atmospherics.T0C+300f; //starts to decompose at 300C
			const float decomposition_halfmark = 1500f; // at this many degrees more than the min, half will decompose in a cycle. (1250C total)
			//these numbers were eyeballed, honestly. finding a source for this is hard.
			
			float currenttemp=mixture.Temperature;
			float totalsystemenergy= atmosphereSystem.GetHeatCapacity(mixture, true)*currenttemp;
			float n2o = mixture.GetMoles(Gas.NitrousOxide);
			
			float conversionratio=GasReactionHelperFunctions.reaction_mult_mintemp_higher_asymptote(currenttemp, min_temp_to_decompose ,decomposition_halfmark );
			
			float totalmolesdecomp=n2o*conversionratio;
			
			if (totalmolesdecomp>0.0f){
				totalsystemenergy+=totalmolesdecomp*energy_per_mole_decomposed/heatScale;
				mixture.AdjustMoles(Gas.NitrousOxide,  -totalmolesdecomp  );
				mixture.AdjustMoles(Gas.Nitrogen,  totalmolesdecomp  );
				mixture.AdjustMoles(Gas.Oxygen,  totalmolesdecomp*0.5f  );
				
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