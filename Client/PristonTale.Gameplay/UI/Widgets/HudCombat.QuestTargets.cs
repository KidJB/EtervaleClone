using Microsoft.Xna.Framework;
using PristonTale.Contracts.World;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Quests;

namespace PristonTale.Gameplay.UI.Widgets;

// A bobbing purple orb over every monster in view whose kill counts for the running quest (the server's QuestTargets). A
// player's summons never count.
public sealed partial class HudCombat
{
    public const int NoField = -2;
    private const int QuestTargetLift = 62;
    private const int QuestTargetBob = 4;
    private const double QuestTargetBobMs = 260d;
    private const int QuestTargetSize = 16;

    private readonly HashSet<uint> _questTemplates = [];
    private bool _questAnyMonster;
    private int _questField = QuestHunt.AnyField;

    // The player's field code (NoField while none is loaded) and the HUD clock that bobs the marks.
    public int FieldCode { get; set; } = NoField;
    public double ClockMs { get; set; }

    public void SetQuestTargets(QuestView view)
    {
        _questTemplates.Clear();
        _questAnyMonster = view?.Hunt.AnyMonster == true;
        _questField = view?.Hunt.Field ?? QuestHunt.AnyField;
        if (view is not null)
        {
            _questTemplates.UnionWith(view.TargetTemplates);
        }
    }

    public bool IsQuestTarget(uint templateId, int fieldCode) =>
        (_questField == QuestHunt.AnyField || _questField == fieldCode) && (_questAnyMonster || _questTemplates.Contains(templateId));

    private void DrawQuestTargets(HudPainter painter, HudLayout layout, Func<ulong, HudOverheadPoint?> project)
    {
        if (!_questAnyMonster && _questTemplates.Count == 0)
        {
            return;
        }

        var scale = Math.Max(1, layout.Scale);
        var bob = (int)(MathF.Sin((float)(ClockMs / QuestTargetBobMs)) * QuestTargetBob);
        foreach (var (id, entity) in _entities)
        {
            if (entity.Spawn.Kind == EntityKind.Monster && entity.Spawn.OwnerId == 0 && entity.Life > 0 && IsQuestTarget(entity.Spawn.TemplateId, FieldCode)
                && project(id) is { } point && point.Depth < HudNameColors.OverheadDepth)
            {
                var x = (int)(point.Screen.X / scale) - QuestTargetSize / 2;
                var y = (int)(point.Screen.Y / scale) - QuestTargetLift + bob - QuestTargetSize / 2;
                painter.Stretch(HudQuestBook.TargetMark, new Rectangle(x, y, QuestTargetSize, QuestTargetSize));
            }
        }
    }
}
