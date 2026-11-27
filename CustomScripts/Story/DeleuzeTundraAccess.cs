/*
name: Deleuze Tundra Access
description: Complete the Nation story through Self-Destructive Spell and verify access to Blighted Zubami.
tags: nation, deleuzetundra, story, hdbon
*/
//cs_include Scripts/CoreBots.cs
//cs_include Scripts/CoreStory.cs
//cs_include Scripts/Story/Nation/VoidChasm.cs

using Skua.Core.Interfaces;

public class DeleuzeTundraAccess
{
    private const string Map = "deleuzetundra";
    private const int Prerequisite = 9552; // Famiglia di Carcano
    private const int AccessQuest = 10030; // Self-Destructive Spell
    private IScriptInterface Bot => IScriptInterface.Instance;
    private CoreBots Core => CoreBots.Instance;
    private readonly CoreStory _story = new();

    public void ScriptMain(IScriptInterface bot)
    {
        Core.SetOptions();
        try
        {
            new VoidChasm().Storyline();
            if (Bot.ShouldExit || !Core.isCompletedBefore(Prerequisite))
                return;

            _story.KillQuest(10023, Map, "Empty Creature");
            _story.KillQuest(10024, Map, "Nation Outrider");

            if (!CompleteMixedQuest(10025, [(14048, 5), (14049, 1)]))
                return;

            _story.KillQuest(10026, Map, "Oblivion Magus");

            if (!CompleteMixedQuest(10027, [(14050, 1), (14051, 1)])
                || !CompleteMixedQuest(10028, [(14052, 1)], "Empty Creature", "Void Heart", 3)
                || !CompleteMixedQuest(10029, [(14053, 5)], "Nation Outrider", "Blunt Blade", 15)
                || !CompleteMixedQuest(AccessQuest, [(14054, 1)], "Oblivion Magus", "Obliteration Rune", 15))
                return;

            Core.Join(Map);
            Core.Jump("r8", "Left");
            if (Bot.Wait.ForTrue(() => Bot.Flash.Call("availableMonsters")?.Contains("Blighted Zubami") == true, 20))
                Core.Logger("DELEUZE_STORY: Zubami access verified.");
            else
                Core.Logger("DELEUZE_STORY: quest complete, but the Zubami target did not load.");
        }
        finally
        {
            Core.SetOptions(false);
        }
    }

    private bool CompleteMixedQuest(
        int questId, (int ItemID, int Quantity)[] mapItems,
        string? monster = null, string? drop = null, int quantity = 0)
    {
        if (Bot.ShouldExit)
            return false;
        if (_story.QuestProgression(questId))
            return true;

        if (Bot.Map.Name != Map)
            Core.Join(Map);
        Core.EnsureAccept(questId);
        if (monster != null && drop != null)
            Core.HuntMonster(Map, monster, drop, quantity);
        if (Bot.ShouldExit)
            return false;
        Core.GetMapItems(mapItems, Map);
        Core.EnsureComplete(questId);
        return !Bot.ShouldExit && Core.isCompletedBefore(questId);
    }
}
