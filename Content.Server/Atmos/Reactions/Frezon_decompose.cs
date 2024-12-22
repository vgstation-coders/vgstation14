using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Reactions;
using JetBrains.Annotations;

using Content.Server.Atmos.GasReactionHelpers;

namespace Content.Server.Atmos.Reactions
{
    [UsedImplicitly]
    [DataDefinition]
    public sealed partial class FrezonDecomposition : IGasReactionEffect
    {
        public ReactionResult React(GasMixture mixture, IGasMixtureHolder? holder, AtmosphereSystem atmosphereSystem, float heatScale)
        {
			const float minimum_temp = Atmospherics.T0C; // will break down above 0C
			const float halfstep_temp = 1000f;
			
			float currenttemp=mixture.Temperature;
			float frez = mixture.GetMoles(Gas.Frezon);

			if(frez<=0f){
				return ReactionResult.NoReaction;
			}
			
			float conversionratio=GasReactionHelperFunctions.reaction_mult_mintemp_higher_asymptote(currenttemp, minimum_temp ,halfstep_temp );
			
			if (conversionratio>0f){
				float molestoreact=frez*conversionratio;
				
				mixture.AdjustMoles(Gas.Frezon, -molestoreact); //break down into oxy. this is done because it's its originating gas
				mixture.AdjustMoles(Gas.Oxygen, molestoreact); //but also, because it makes it so that frezon cans don't just evaporate into nothing.

				return ReactionResult.Reacting;
			}
			return ReactionResult.NoReaction;



		
		}
	}
}