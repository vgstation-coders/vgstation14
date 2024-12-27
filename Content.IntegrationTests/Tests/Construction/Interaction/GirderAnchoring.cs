using Content.IntegrationTests.Tests.Interaction;

namespace Content.IntegrationTests.Tests.Construction.Interaction;

public sealed class GirderAnchoring : InteractionTest
{
    public const string Girder = "Girder";

    [Test]
    public async Task UnanchorReanchorGirder()
    {
        await StartConstruction(Girder);
        await InteractUsing(Steel, 2);
        Assert.That(Hands.ActiveHandEntity, Is.Null);
        ClientAssertPrototype(Girder, Target);
        await InteractUsing(Pry);
        AssertAnchored(false);

        await InteractUsing(Pry);
        AssertAnchored();
    }
}
