using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Gameplay.UI.Chat;
using PristonTale.Gameplay.UI.HelpPet;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Npc;
using PristonTale.Gameplay.UI.Parties;
using PristonTale.Gameplay.UI.PersonalShops;
using PristonTale.Gameplay.UI.Quests;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Gameplay.UI.Travel;
using PristonTale.Gameplay.UI.Widgets;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.Quests;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;
using PristonTale.Gameplay.Localization;
using PristonTale.Gameplay.UI.Clans;

namespace PristonTale.Gameplay.UI;

// The original in-game interface in its draw order: speech bubbles, the chat window, status, skill, inventory, quest and
// party windows behind the bottom bar, then item overlays, help boxes and message boxes. The system menu opens the
// settings, the help window or the game's exit. Refusals show in the notice strip; confirmations and server lines go to
// the chat.
public sealed partial class GameplayHud : IDisposable
{
    private readonly ScreenContext _context;
    private readonly HudSprites _sprites;
    private readonly HudPainter _painter;
    private readonly HudItemIcons _icons;
    private readonly Dictionary<Guid, string> _itemKeys = new();
    private readonly HudSkillBindings _bindings = new();
    private readonly HudActionBar _actionBar;
    private readonly HudActionBarStore _actionBarStore;
    private int _actionBarRefusals;
    private int _savedSlots = -1;
    private int _savedBar = -1;
    private readonly HudBar _bar = new();
    private readonly HudGroundLabels _dropNames = new();
    private readonly HudStatusPanel _status = new();
    private readonly HudSkillsPanel _skills;
    private readonly HudSkillHud _skillHud;
    private static readonly CharacterClassRegistry SkillClasses = new();
    private PlayerClass _class;
    private IReadOnlyList<OwnedSkillView> _syncedSkills;
    private readonly HudInventoryPanel _inventoryPanel = new();
    private readonly HudInventoryController _inventory;
    private readonly HudProgressionController _progression;
    private readonly HudNpcPanel _npc;
    private readonly HudNpcServiceController _services;
    private readonly HudShopWindow _shop = new();
    private readonly HudCountBox _countBox = new();
    private readonly HudWarehouseWindow _warehouse = new();
    private readonly HudClanWarehouseWindow _clanWarehouse = new();
    // StoredPlacement as a delegate made once, for the warehouse's per-frame update.
    private readonly Func<OwnedItemView, Point, HudPlacement> _storedPlacement;
    private readonly HudAgingWindow _aging = new();
    private readonly HudMixWindow _mix = new();
    private readonly HudSmeltingWindow _smelting = new();
    private readonly HudManufactureWindow _manufacture = new();
    private readonly HudMixtureResetWindow _mixtureReset = new();

    // The crafting windows, each open while its NPC service is.
    private readonly HudWorkbench[] _benches;
    // The service windows in draw order: bodies, then overlays, shop first.
    private readonly IHudServiceWindow[] _serviceWindows;
    private readonly HudQuestController _quests;
    private readonly HudQuestDocuments _questDocuments;
    private readonly HudQuestDialog _questDialog = new();
    private readonly HudQuestWindow _questWindow = new();
    private QuestView _questView;
    private double _questViewMs;
    // The quest weapon's hunt line for its item box, made when the quest view changes.
    private string _weaponHunt = "";
    private int _questCompletions;
    private int _questDialogs;
    private readonly HudSettingsPanel _settings;
    private readonly HudAchievements _achievements = new();
    private readonly HudRankings _rankings = new();
    private readonly HudPartyFinder _partyFinder = new();
    private readonly HudMinimap _minimap = new();
    private readonly HudBossTime _bossTime = new();
    private const string AutoSkillArt = "Hud/autoskill.png";
    private readonly HudWorldBuffs _worldBuffs = new();
    private int _savedBuffs;
    private int _savedParty;
    private readonly HudBossList _bossList = new();
    private readonly HudPetWindow _petWindow = new();
    private readonly HudQuestBook _questBook = new();
    private readonly HudQuestTracker _questTracker = new();
    private readonly HudCoinShop _coinShop = new();
    private readonly Npc.HudItemBox _itemBox = new();
    private readonly Npc.HudArenaEvent _arenaEvent = new();
    private readonly Npc.HudArenaTimer _arenaTimer = new();
    private readonly Npc.HudCabinet _cabinet = new();
    private readonly Markets.HudMarketplace _marketplace = new();
    private readonly Markets.HudCharacterMarket _characterMarket = new();
    private static readonly Color BagCoinColor = new(255, 214, 90);
    private bool _bagWasOpen;
    private int _iconSources;
    private readonly Dictionary<string, string> _npcQuestMarks = new(StringComparer.Ordinal);
    private bool _marksHuntComplete;
    private readonly List<HudMapMarker> _minimapMarkers = new(64);
    private int _savedMap = -1;
    private int _savedTracker = -1;
    private readonly HudTargetPortrait _portrait;
    private readonly HudNotice _notice = new();
    private readonly HudChat _chat = new();
    private readonly HudSlide _inventorySlide = new();
    private readonly HudSlide _statusSlide = new();
    private readonly HudSlide _skillSlide = new();
    private readonly HudItemUseController _itemUse;
    private readonly HudPremiumTimers _premium;
    private readonly HudPremiumKeeps _premiumKeeps = new();
    private readonly HudPuzzleExchange _puzzle = new();
    private readonly HudCrystalDismiss _crystalDismiss = new();
    private readonly HudCaravanDismiss _caravanDismiss = new();
    private PristonTale.Client.Net.Presence.PetCollectionInfo _caravanShownFor;
    private readonly HudRestartPanel _restart;
    private readonly HudWingGate _wingGate;
    private int _itemUseCompletions;
    private int _wingGateCompletions;
    private int _shopsOpened;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HudQuestBoxGlow _questBoxGlow = new();
    // Delegates the frame passes, made once: the speakers' name colour, a typing box's key test (on this frame's keys)
    // and the painter's glyph and width tests.
    private Func<ulong, Color> _nameColor;
    private Func<Keys, bool> _typedKey;
    private KeyboardState _typingKeyboard;
    private Func<char, bool> _canDraw;
    private Func<string, int> _measure;
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private OwnerState _state;
    private HudMenuButton? _hoveredButton;
    private HudToggle? _hoveredToggle;
    private ulong _skillMaster;
    private bool _systemMenu;
    private bool _settingsOpen;
    private bool _npcOpen;
    private bool _mapVisible = true;
    private bool _ready;
    private int _inventoryCompletions;
    private int _progressionCompletions;
    private int _serviceCompletions;
    private int _bindingRefusals;
    private int _serviceOpened;
    private int _hoveredTeleportFieldCode = -1;
    private double _hoveredTeleportSince;
    private bool _teleportFieldPrepared;
    private double _lastUpdate;
    private readonly Action<int> _prepareField;

    public GameplayHud(ScreenContext context, Func<InventoryCommand, Task<InventoryOutcome>> applyInventory,
        Func<ProgressionCommand, Task<ProgressionOutcome>> applyProgression = null,
        Func<ulong, Task<NpcInteraction>> interactNpc = null,
        Action<NpcInteraction> npcOpened = null,
        Action npcClosed = null,
        Func<NpcServiceCommand, Task<NpcServiceReply>> requestNpcService = null,
        Action<int> prepareField = null,
        Func<QuestCommand, Task<QuestOutcome>> requestQuest = null)
    {
        _context = context;
        _clanWindow = new HudClanWindow(context.Log);
        _castleWindow.AskAmount = AskWithdrawal;
        _bellatraWindow.AskAmount = AskRedemption;
        _settings = new HudSettingsPanel(context.Presentation);
        _sprites = new HudSprites(context.GraphicsDevice, context.AssetRoot, context.Log);
        _sprites.Request(HudArt.Static);
        _sprites.Request([.. HudMinimap.Art, .. Npc.HudArenaEvent.Art, .. Npc.HudArenaTimer.Art, .. Npc.HudCabinet.Art, .. Npc.HudClanWarehouseWindow.Art, .. HudWorldBuffs.Art, .. HudCombat.TargetBarArt, AutoSkillArt, .. HudQuestBook.Art, .. HudQuestTracker.Art, .. HudPetWindow.Art, .. Inventory.HudItemFrame.Art, Widgets.HudGroundLabels.BeamArt, .. HudCoinShop.Art, .. Npc.HudItemBox.Art, .. Markets.HudMarketplace.Art, .. Markets.HudCharacterMarket.Art, .. HudCourierWindow.Art]);
        _arenaEvent.Teleport = () => _ = _chat.Send?.Invoke("/arena enter", false, Guid.Empty);
        _sprites.Request(HudNpcArt.All);
        _sprites.Request(HudWarehouseWindow.CaravanArt);
        _sprites.Request(HudCraftArt.All);
        _sprites.Request(HudArt.BlockText);
        _sprites.Request(HudQuestArt.All);
        _sprites.Request(Bellatra.HudBellatraResult.Art);
        SetUpBellatraShow();
        _painter = new HudPainter(context.SpriteBatch, context.Font, _sprites);
        _icons = new HudItemIcons(context.GraphicsDevice, context.AssetRoot, context.Log);
        _icons.Request([PetSkins.IconKey]);
        _personalShop = new HudPersonalShopController(context.Log) { Notify = _notice.Show, Command = line => _ = _chat.Send?.Invoke(line, false, Guid.Empty), AskCount = (first, second, maximum, accepted) => _countBox.Open(first, second, maximum, accepted) };
        _benches = [_aging, _mix, _smelting, _manufacture, _mixtureReset];
        _serviceWindows = [_shop, _warehouse, _clanWarehouse, .. _benches, _personalShop.Window, _trade];
        _sprites.Request(HudPersonalShopArt.All);
        _skills = new HudSkillsPanel(context.Font, _sprites);
        _skillHud = new HudSkillHud(_sprites, context.GraphicsDevice);
        _inventory = new HudInventoryController(context, applyInventory);
        _progression = new HudProgressionController(applyProgression, context.Log);
        _npc = new HudNpcPanel(interactNpc, context.Log, npcOpened, npcClosed);
        _portrait = new HudTargetPortrait(context.GraphicsDevice);
        _services = new HudNpcServiceController(requestNpcService, context.Log);
        _prepareField = prepareField;
        _itemUse = new HudItemUseController(UseItemAsync, context.Log, _sprites);
        _actionBar = new HudActionBar(_bindings);
        _actionBarStore = new HudActionBarStore(HudActionBarStore.DefaultDirectory, context.Log);
        _bindings.ItemRequested += UseShortcutItem;
        _restart = new HudRestartPanel(context.Log);
        _premium = new HudPremiumTimers(_sprites);
        _sprites.Request(HudPteuWindow.Art);
        _wingGate = new HudWingGate(context.Log);
        _sprites.Request(HudWingGate.Art);
        _inventory.UseRequested = item => _itemUse.Request(item, _state);
        _quests = new HudQuestController(requestQuest, context.Log);
        _inventory.QuestItemUse = _quests.UseItem;
        _questDocuments = new HudQuestDocuments(context.AssetRoot, context.Log);
        _inventory.InterceptClick = InterceptInventoryClick;
        _inventory.ExternalDrop = DropFromInventory;
        _inventory.ThrowRequested = ThrowFromInventory;
        _inventory.GoldRequested = GoldFromInventory;
        _inventory.LockRefused = () => _notice.Show(ClientText.Get("Inventory.Locked"));
        // The special item box opens and closes but not while a service window is open, which closes it.
        _inventory.QuestBoxRequested = () =>
        {
            if (!ServiceWindowOpen(blockingOnly: false))
            {
                _inventory.ToggleQuestBox();
            }
        };
        _inventory.ItemSound = key => ItemSound?.Invoke(key);
        _inventory.QuestBoxStored = frame =>
        {
            _questBoxGlow.Start(frame, _clock.Elapsed.TotalMilliseconds);
            QuestFanfare?.Invoke();
        };
        _services.Succeeded = ServiceSucceeded;
        _storedPlacement = StoredPlacement;
        _inventoryPanel.Note = InventoryNote;
        _inventoryPanel.Hunt = item => item.Code == QuestClasses.QuestWeapon(_class) ? _weaponHunt : "";
        _inventoryPanel.Withheld = id => BenchHolds(id) || _personalShop.Holds(id) || _trade.Holds(id);
        _inventory.ShopRequested = OpenOwnShop;
        _sprites.Request(HudChatWindow.Art);
        _sprites.Request(HudActionBar.Art);
        _sprites.Request(HudMessageBox.Art);
        _sprites.Request(HudSettingsPanel.Art);
        _cabinet.Favorites = context.Presentation.Requested.CabinetFavorites;
        _cabinet.FavoritesChanged = favorites => context.Presentation.Requested = context.Presentation.Requested with { CabinetFavorites = favorites };
        _sprites.Request(HudAchievements.Art);
        _sprites.Request(HudRankings.Art);
        _sprites.Request(HudPartyFinder.Art);
        _sprites.Request(HudChatBubbles.Art);
        _chat.Lookup = id => (Combat.KindOf(id, out var owner), owner);
        _helpPet = new HudHelpPet(HelpPetMessages.LoadAsync(context.AssetRoot, context.Log, ClientText.Language), new Random(), context.Log,
            language => HelpPetMessages.LoadAsync(context.AssetRoot, context.Log, language));
        _sprites.Request(HudHelpPet.Art);
        SetUpParty();
        SetUpTrade();
        SetUpContacts();
        SetUpClans();
        SetUpHelp();
    }

    // The chat window, its input line and the speech bubbles.
    public HudChat Chat => _chat;

    // The chat part of each presence update (server lines, whispers, notices, the close request).
    public void ObserveChat(PristonTale.Client.Net.Presence.PresenceUpdate update) => _chat.Observe(update, _clock.Elapsed.TotalMilliseconds);

    // The input line has the keyboard: game keys must not act on typed letters.
    public bool KeyboardCaptured => _chat.KeyboardCaptured;

    // Where a speaker's bubble hangs on screen, from the scene; null when the speaker is not drawn.
    public Func<ulong, HudOverheadPoint?> Overhead { get; set; } = static _ => null;

    public HudCombat Combat { get; } = new();
    public HudStringEffect StringEffect { get; } = new();

    // Uses a carried item through the server; set by the screen.
    public Func<PristonTale.Simulation.Items.Usable.ItemUseCommand, Task<PristonTale.Simulation.Items.Usable.ItemUseOutcome>> UseItem { get; set; }

    // The field's next boss pass as the server last sent it; the minimap counts down from when it arrived.
    // The local player's ranking aura, shown with its bonus in the Active Buffs box.
    public Contracts.Characters.RankAura RankAura
    {
        set => _worldBuffs.Rank(value, DataText.ClassTitle(_class, 0));
    }

    public IReadOnlyList<(int Kind, int SecondsLeft)> WorldBuffs
    {
        set => _worldBuffs.Receive(value, _clock.Elapsed.TotalMilliseconds);
    }

    public PristonTale.Client.Net.Presence.BossTimeInfo NextBoss
    {
        get => _bossTime.Info;
        set => _bossTime.Receive(value, _clock.Elapsed.TotalMilliseconds);
    }

    // The bosses of the player's field at their spot (grey until alive, red while alive on the minimap).
    public IReadOnlyList<PristonTale.Client.Net.Presence.BossSpotInfo> BossSpots { get; set; } = [];
    public IReadOnlyList<PristonTale.Client.Net.Presence.SpawnSpotInfo> SpawnSpots { get; set; } = [];

    // The player is in a town where personal premium timers stand still.
    public bool PremiumPaused { get; set; }

    // The local player's pet collection, for the pet window.
    public PristonTale.Client.Net.Presence.PetCollectionInfo PetCollection { get; set; } = new(0, 0);
    public PristonTale.Client.Net.Presence.DailyQuestsInfo DailyQuests { get; set; } = PristonTale.Client.Net.Presence.DailyQuestsInfo.None;

    // Sends a daily quest accept or claim; set by the screen.
    public Func<ulong, int, byte, Task<byte>> DailyQuestRequest { get; set; }
    private Task<byte> _dailyAnswer;

    // The names over the pets in view, filled by the scene each frame (GameScene.CollectPetLabels).
    public List<(HudOverheadPoint Point, string Text)> PetLabels { get; } = new();

    // The names over the caravans in view and the point over the local player's own, filled by the scene each frame
    // (GameScene.CollectCaravanLabels); a click on the own one opens its storage.
    public List<(HudOverheadPoint Point, string Text)> CaravanLabels { get; } = new();
    public HudOverheadPoint? OwnCaravan { get; set; }

    // Every scheduled boss of the world, for the boss list beside the minimap.
    public IReadOnlyList<PristonTale.Client.Net.Presence.BossListInfo> BossList
    {
        set => _bossList.Receive(value, _clock.Elapsed.TotalMilliseconds);
    }

    // The local player's running premium timers as the server last sent them.
    public PristonTale.Client.Net.Presence.PremiumTimerSnapshot PremiumTimers { get; set; }
    public ulong TargetId { get; set; }
    // The entity under the cursor and the clicked one, for the name boxes over the scene.
    public ulong HoveredId { get; set; }
    public ulong TracedId { get; set; }
    // The drop names to draw under the entity names, and where each one landed (back-buffer pixels) for clicks on it.
    public IReadOnlyList<GroundLabel> GroundLabels { get; set; } = [];
    public IReadOnlyList<GroundBeam> GroundBeams { get; set; } = [];

    // The armour under the pointer in the bag window ("" none) and the scene's picture of the player wearing it.
    public string PreviewArmorKey => _inventorySlide.FullyOpen ? _inventoryPanel.PreviewArmorKey : "";

    public Microsoft.Xna.Framework.Graphics.Texture2D ArmorPreview
    {
        set
        {
            _inventoryPanel.ArmorPreview = value;
            _cabinet.Picture = value;
        }
    }
    public Action<Guid, Rectangle> GroundLabelPlaced { get; set; }
    public HudWorldView World { get; set; }

    // The player stands in a castle field, where a Restoration revives.
    public bool InCastleField { get; set; }

    // The local player is down, and what standing up would cost (from the server).
    public bool PlayerDead { get; set; }
    public bool PlayerActing { get; set; }
    public long PlayerActionStarts { get; set; }
    public RestartOffer? RestartOffer { get; set; }

    // The restart window; the screen sets its request and quits on its exit choice.
    public HudRestartPanel Restart => _restart;

    // The wing warp map a warp stone opens; the screen opens it and sets its trip.
    public HudWingGate WingGate => _wingGate;

    public HudCoinShop CoinShop => _coinShop;
    public Npc.HudItemBox ItemBox => _itemBox;
    public Npc.HudCabinet Cabinet => _cabinet;
    public Markets.HudMarketplace Marketplace => _marketplace;
    public Markets.HudCharacterMarket CharacterMarket => _characterMarket;

    // The account's coins, shown by the bag, the trade window and the coin shop; the screen sets its request.
    public HudCoinBalance Coins => _coinShop.Coins;
    public IHudPortraitScene PortraitScene { get; set; }
    public bool PortraitReady => _portrait.Texture(TargetId) is not null;
    public float MinimapViewSize => _minimap.CurrentViewSize;
    public bool PointerCaptured { get; private set; }
    public bool AssetsFailed => _sprites.Failed || _icons.Failed;
    // While the world's entry waits for the HUD's art, its images upload with a larger budget per frame.
    public bool EagerLoading { set => _sprites.Eager = value; }

    public bool IconsIdle => _icons.IsIdle && _sprites.IsIdle && !_questDocuments.IsLoading && !_helpTexts.IsLoading;
    public long TextureBytes => _sprites.TextureBytes;
    // While auto skill runs its ring circles the right skill socket.
    public bool AutoSkill { get; set; }

    public bool RightAttackEnabled => _bindings.RightSkillId == HudSkillBindings.BasicAttackId;
    internal uint LeftSkillId => _bindings.LeftSkillId;
    internal HudItemUseController ItemUse => _itemUse;
    internal HudPuzzleExchange Puzzle => _puzzle;
    internal uint RightSkillId => _bindings.RightSkillId;
    public HudSkillBindings Bindings => _bindings;

    // The local player's statuses and cooldowns from the server's skill messages, and the clock they are kept on
    // (GameScene.Skills).
    public PristonTale.Gameplay.Effects.StatusBook Statuses { get; set; }
    public PristonTale.Gameplay.Effects.SkillCooldownBook Cooldowns { get; set; }
    public double SkillClockMs { get; set; }
    public ulong SelfId { get; set; }

    // A double click on a summon's buff icon asks to switch the summon's fighting mode.
    public event Action<uint> SummonModeRequested;

    // The owner confirmed dismissing the pet of one status (the code of its icon).
    public event Action<uint> CrystalPetDismissRequested;

    // R or the bar's walk/run button.
    public event Action RunToggleRequested;

    // Quit in the exit button's menu.
    public event Action ExitRequested;

    public event Action YahooKeyPressed;

    // The fighting mode the player chose for a running summon skill (true: automatic), or null for other skills.
    public Func<uint, bool?> SummonAutomatic { get; set; }
    private uint _lastBuffClicked;
    private double _lastBuffClickSeconds;

    // The quest fanfare (the drink sound): a quest started, moved on or ended, or a carried item set into the closed
    // special item box.
    public event Action QuestFanfare;

    public event Action<bool> CraftResult;

    // An item lifted, set, bought, sold, stored or taken back, by catalog key, for its sound.
    public event Action<string> ItemSound;

    // The quest part of each presence update (the journal, quest notices and dialogs the world opened).
    public void ObserveQuests(PristonTale.Client.Net.Presence.PresenceUpdate update) => _quests.Observe(update);

    public bool IsReady
    {
        get
        {
            _ready = _ready || HudArt.Static.All(_sprites.IsResolved);
            return _ready;
        }
    }

    public PlayerClass CharacterClass
    {
        set
        {
            _class = value;
            _syncedSkills = null;
            _skillHud.SetClass(value);
            _skills.SetClass(value);
            _status.ClassName = DataText.ClassTitle(value, 0);
            _status.Invalidate();
        }
    }

    public string CharacterName
    {
        get => _status.Name;
        set
        {
            _status.Name = value;
            _status.Invalidate();
            _actionBarStore.Load(value);
        }
    }

    public void OpenNpc(ulong entityId) => _npc.Request(entityId);

    // A ground item picked up with the inventory open comes to its cursor (HudInventoryController.ExpectPickup).
    public bool ExpectPickup(uint itemCode) => _inventory.ExpectPickup(itemCode);

    public void PickupAnswered(bool ok, long revision) => _inventory.PickupAnswered(ok, revision);

    public bool ClosePanel()
    {
        if (_countBox.IsOpen)
        {
            _countBox.Close();
            return true;
        }

        if (_personalShop.ModalOpen)
        {
            return _personalShop.Close();
        }

        if (_bossList.Open)
        {
            _bossList.Close();
            return true;
        }

        // Escape leaves the rename box first, then closes the window.
        if (_petWindow.Naming)
        {
            _petWindow.CancelNaming();
            return true;
        }

        if (_petWindow.Open)
        {
            _petWindow.Close();
            return true;
        }

        if (_questBook.Open)
        {
            _questBook.Close();
            return true;
        }

        if (_coinShop.Open)
        {
            _coinShop.Close();
            return true;
        }

        // Escape leaves the distributor's name box first, then closes it.
        if (_marketplace.Searching || _characterMarket.Typing)
        {
            _marketplace.CancelSearch();
            _characterMarket.CancelTyping();
            return true;
        }

        if (_marketplace.Open || _characterMarket.Open)
        {
            _marketplace.Hide();
            _characterMarket.Hide();
            return true;
        }

        if (_cabinet.Searching)
        {
            _cabinet.CancelSearch();
            return true;
        }

        if (_itemBox.Naming)
        {
            _itemBox.CancelNaming();
            return true;
        }

        if (_itemBox.Open)
        {
            _itemBox.Hide();
            return true;
        }

        if (_arenaEvent.Open)
        {
            _arenaEvent.Hide();
            return true;
        }

        if (_cabinet.Open)
        {
            _cabinet.Hide();
            return true;
        }

        // As in the original, Escape first closes an open chat window and nothing else.
        if (_chat.Window.Open && !_settingsOpen)
        {
            _chat.Window.Close();
            return true;
        }

        _npc.Dismiss();
        var closed = _clanWindow.IsOpen || _castleWindow.IsOpen || _siegeResult.IsOpen || _bellatraWindow.IsOpen || _bellatraResult.IsOpen || _npcOpen || _settingsOpen || _systemMenu || _skills.TrainingPending
            || _inventorySlide.Open || _statusSlide.Open || _skillSlide.Open || ServiceWindowOpen(blockingOnly: false)
            || _questWindow.Open || _partyWindow.Open || _quests.Dialog is not null || _quests.SkillResetBox;
        closed |= _personalShop.Close();
        CloseServices();
        closed |= _itemUse.Teleport.IsOpen || _itemUse.Confirming || _puzzle.Confirming || _crystalDismiss.Confirming || _caravanDismiss.Confirming || _wingGate.IsOpen || _wingGate.Confirming
            || _helpWindow.IsOpen;
        _helpWindow.Close();
        _clanWindow.Close();
        _castleWindow.Close();
        _siegeResult.Close();
        _bellatraWindow.Close();
        _bellatraResult.Close();
        _itemUse.Close();
        _wingGate.Close();
        _puzzle.Close();
        _crystalDismiss.Close();
        _caravanDismiss.Close();
        _quests.CloseDialog();
        _quests.AnswerSkillReset(false);
        _quests.LeaveNpc();
        _questWindow.Close();
        _partyWindow.Close();
        _partyFrames.Close();
        _social.Hide();
        _tuning.Hide();
        _achievements.Hide();
        _rankings.Hide();
        _partyFinder.Hide();
        _courierWindow.Hide();
        _npcOpen = false;
        _settingsOpen = false;
        _systemMenu = false;
        _inventorySlide.Open = false;
        _statusSlide.Open = false;
        _skillSlide.Open = false;
        _skillMaster = 0;
        _skills.CancelTraining();
        _inventory.CancelDrag();
        return closed;
    }

    public void Update(OwnerState state)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var elapsed = now - _lastUpdate;
        _lastUpdate = now;
        Update(state, Keyboard.GetState(), Mouse.GetState(), elapsed);
    }

    internal void Update(OwnerState state, KeyboardState keyboard, MouseState mouse, double elapsedSeconds = 1)
    {
        _sprites.Update();
        _icons.Update();
        _progression.Update();
        _services.Update();
        _itemUse.Update();
        _wingGate.Update();
        _itemUse.Restore(state, InCastleField, _clock.Elapsed.TotalMilliseconds);
        _restart.Update(PlayerDead, _clock.Elapsed.TotalMilliseconds);
        _premium.Apply(PremiumTimers, _clock.Elapsed.TotalMilliseconds);
        if (!ReferenceEquals(_caravanShownFor, PetCollection))
        {
            _caravanShownFor = PetCollection;
            _premium.SetCaravan(PetCollection.Caravan, PetCollection.CaravanEnds, PetCollection.CaravanName, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                _clock.Elapsed.TotalMilliseconds);
        }
        _premium.Hold(PremiumPaused, _clock.Elapsed.TotalMilliseconds);
        _quests.Update();
        _npc.Update(_context.Font);
        _minimap.Update(_sprites, World, elapsedSeconds);
        _notice.Update(elapsedSeconds);
        StringEffect.Update((float)elapsedSeconds);
        _inventorySlide.Advance(elapsedSeconds);
        _statusSlide.Advance(elapsedSeconds);
        _skillSlide.Advance(elapsedSeconds);
        foreach (var window in _serviceWindows)
        {
            window.Advance(elapsedSeconds);
        }

        _questWindow.Advance(elapsedSeconds);
        _partyWindow.Advance(elapsedSeconds);
        _clanWindow.Update();
        _partyRequest.Update(_clock.Elapsed.TotalMilliseconds);
        _clanRequest.Update(_clock.Elapsed.TotalMilliseconds);
        _tradeRequest.Update(_clock.Elapsed.TotalMilliseconds);
        UpdateContacts(_clock.Elapsed.TotalMilliseconds);
        _bellatraResult.Update(_clock.Elapsed.TotalMilliseconds, _npcOpen);
        _bellatraShow?.Update(elapsedSeconds);
        ForwardMessages();
        ApplyQuests();
        _coinShop.OwnClass = _class;
        _coinShop.Update(_icons, _sprites);
        _itemBox.Update();
        _cabinet.Jobs = (uint)JobBits.Of(_class);
        _cabinet.Update(_state, elapsedSeconds);
        _marketplace.Update(_state);
        _characterMarket.Update(_state);
        UpdateDailyQuests();
        var iconSources = _cabinet.IconRevision * 31 + _itemBox.IconRevision + _coinShop.IconRevision * 7919 + _personalShop.Window.IconRevision * 104729
            + _marketplace.IconRevision * 13 + _characterMarket.IconRevision * 17 + (_cabinet.Open ? 8 : 0) + (_itemBox.Open ? 1 : 0) + (_coinShop.Open ? 2 : 0) + (_personalShop.Window.IsOpen ? 4 : 0);
        if (iconSources != _iconSources)
        {
            _iconSources = iconSources;
            RequestIcons(_state);
        }
        if (_inventorySlide.Open && !_bagWasOpen)
        {
            Coins.Refresh();
        }

        _bagWasOpen = _inventorySlide.Open;
        Coins.Poll();
        ApplyServices(state);
        _personalShop.SignShown = SelfId != 0 && ShopSigns.ContainsKey(SelfId);
        _personalShop.Update(state, _clock.Elapsed.TotalMilliseconds);
        if (_npc.ReadyToOpen)
        {
            _skillMaster = 0;
            _skills.CancelTraining();
            _settingsOpen = false;
            CloseServices();
            // While the previous window's close is still in flight the dialog shows, and its button opens the service.
            var direct = _npc.DirectService;
            _npcOpen = direct is null || !OpenNpcService(_npc.Current.EntityId, _npc.Current.Name, direct.Kind);
            _quests.LeaveNpc();
            if (_npc.TakesPartInQuests)
            {
                _quests.Talk(_npc.Current.EntityId);
            }
        }

        var layout = Layout();
        HudMessageBox.Place(layout);
        var pointer = layout.ToLogical(mouse.Position);
        var design = layout.ToDesign(pointer);
        UpdateTeleportPrefetch(pointer);
        if (_quests.Dialog is not null && HudQuestDialog.Box.Contains(pointer))
        {
            _questDialog.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_social.Contains(layout, pointer))
        {
            _social.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_clanWindow.Contains(pointer))
        {
            _clanWindow.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_partyWindow.Page == HudPartyPage.Clan && _partyWindow.Contains(layout, pointer))
        {
            _partyWindow.Clan.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_rankings.Contains(layout, pointer))
        {
            _rankings.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_partyFinder.Contains(layout, pointer))
        {
            _partyFinder.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_courierWindow.Contains(layout, pointer))
        {
            _courierWindow.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_npcOpen && HudNpcPanel.Box.Contains(pointer))
        {
            _npc.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (mouse.ScrollWheelValue != _previousMouse.ScrollWheelValue && _helpWindow.Contains(pointer))
        {
            _helpWindow.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_itemUse.Teleport.Contains(pointer))
        {
            _itemUse.Teleport.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue, pointer, _bossList.Count);
        }
        else if (MinimapShown && _bossList.Open && _bossList.Window(layout, _minimap.Area(layout)).Contains(pointer))
        {
            _bossList.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_petWindow.Open && HudPetWindow.Window(layout).Contains(pointer))
        {
            _petWindow.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_marketplace.Captures(layout, pointer))
        {
            _marketplace.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_characterMarket.Captures(layout, pointer))
        {
            _characterMarket.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_cabinet.Open && Npc.HudCabinet.Window(layout).Contains(pointer))
        {
            _cabinet.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_coinShop.Open && HudCoinShop.Window(layout).Contains(pointer))
        {
            _coinShop.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }
        else if (_questBook.Open && HudQuestBook.Window(layout).Contains(pointer))
        {
            _questBook.Scroll(mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue);
        }

        if (state is not null && (_state is null || state.InventoryRevision != _state.InventoryRevision))
        {
            RequestIcons(state);
            if (_trade.IsOpen)
            {
                _trade.Follow(state.Items);
            }
        }

        _state = state;
        RestoreActionBar();
        if (state is not null && !ReferenceEquals(state.Skills, _syncedSkills))
        {
            _syncedSkills = state.Skills;
            _bindings.SyncLearned(SkillClasses.Get(_class).Skills, state.Skills);
        }

        if (_context.Presentation.AwaitingConfirmation)
        {
            PointerCaptured = true;
            _previousKeyboard = keyboard;
            _previousMouse = mouse;
            return;
        }

        _hoveredButton = ButtonAt(design);
        _hoveredToggle = ToggleAt(design);
        UpdateHelpPet(state, layout, pointer, design, mouse, _clock.Elapsed.TotalMilliseconds);
        UpdateHelp(state);
        _chat.Window.Drag(layout, mouse, _previousMouse, pointer, ChatYields(layout));
        _partyFinder.Drag(layout, mouse, _previousMouse, pointer);
        _courierWindow.Drag(layout, mouse, _previousMouse, pointer);
        _courierWindow.Update(_clock.Elapsed.TotalMilliseconds);
        _chat.Macros = _context.Presentation.Requested.ChatMacros;
        var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        // A click the HUD takes stays its own when it closes the window under it (a close button over the ground).
        var clickTaken = clicked && Captures(layout, pointer, design);
        var typedText = _context.TypedText.Drain();
        // A count box or the shop's sign box takes the keyboard: neither the chat nor a game key sees what is typed there.
        var boxTyping = _countBox.IsOpen || _personalShop.SignOpen || _petWindow.Naming || _itemBox.Naming || _cabinet.Searching || _marketplace.Searching || _characterMarket.Typing || _clanWindow.Typing || _partyFinder.Typing || _warehouse.NamingCaravan;
        if (_countBox.IsOpen)
        {
            _typingKeyboard = keyboard;
            _countBox.Type(typedText, _typedKey ??= key => Pressed(_typingKeyboard, key));
        }
        else if (_personalShop.SignOpen)
        {
            _typingKeyboard = keyboard;
            _personalShop.Type(typedText, _typedKey ??= key => Pressed(_typingKeyboard, key), _canDraw ??= _painter.CanDraw, _measure ??= _painter.Measure);
        }
        else if (_marketplace.Searching)
        {
            _marketplace.Type(typedText, elapsedSeconds);
        }
        else if (_characterMarket.Typing)
        {
            _characterMarket.Type(typedText, elapsedSeconds);
        }
        else if (_partyFinder.Typing)
        {
            _partyFinder.Type(typedText, elapsedSeconds);
        }
        else if (_warehouse.NamingCaravan)
        {
            _warehouse.TypeCaravanName(typedText, elapsedSeconds);
            if (Pressed(keyboard, Keys.Enter) || Pressed(keyboard, Keys.Escape))
            {
                _warehouse.StopNamingCaravan();
            }
        }
        else if (_cabinet.Searching)
        {
            _cabinet.Type(typedText, elapsedSeconds);
        }
        else if (_itemBox.Naming)
        {
            _typingKeyboard = keyboard;
            _itemBox.Type(typedText, elapsedSeconds, _typedKey ??= key => Pressed(_typingKeyboard, key));
        }
        else if (_petWindow.Naming)
        {
            _typingKeyboard = keyboard;
            _petWindow.Type(typedText, elapsedSeconds, _typedKey ??= key => Pressed(_typingKeyboard, key));
            if (_petWindow.TakeRequest() is { } command)
            {
                _ = _chat.Send?.Invoke(command, false, Guid.Empty);
            }
        }
        else if (_clanWindow.Typing)
        {
            _typingKeyboard = keyboard;
            _clanWindow.Type(typedText, _typedKey ??= key => Pressed(_typingKeyboard, key), _canDraw ??= _painter.CanDraw);
        }
        else if (_caravanDismiss.Confirming && !_chat.Window.Focused && Pressed(keyboard, Keys.Enter))
        {
            DismissCaravan(_caravanDismiss.Accept());
        }
        else if (_crystalDismiss.Confirming && !_chat.Window.Focused && Pressed(keyboard, Keys.Enter))
        {
            // Enter answers Yes to the dismiss question instead of opening the chat input.
            RequestCrystalPetDismiss(_crystalDismiss.Accept());
        }
        else if (_skills.TrainingPending && !_chat.Window.Focused && Pressed(keyboard, Keys.Enter))
        {
            _skills.AcceptTraining(state, _skillMaster, _progression);
        }
        else if (_quests.SkillResetBox && !_chat.Window.Focused && Pressed(keyboard, Keys.Enter))
        {
            _quests.AnswerSkillReset(true);
        }
        else
        {
            _chat.Update(keyboard, _previousKeyboard, typedText, _clock.Elapsed.TotalMilliseconds, _chat.Window.Log.Tab);
        }

        var typing = boxTyping || _chat.KeyboardCaptured;
        _inventory.SidesLocked = Storage.IsOpen && (_inventory.Dragging != Guid.Empty || Storage.Dragging != Guid.Empty);
        if (!typing)
        {
            UpdateKeys(keyboard);
        }

        var workLocked = _services.Pending || _settingsOpen || ModalOpen;
        // A click that sets a warehouse item down in the bag is not also a pick-up there.
        var storedCarried = Storage.Dragging != Guid.Empty;
        var warehouseAction = Storage.Update(mouse, _previousMouse, pointer, _storedPlacement, workLocked, _inventory.Dragging != Guid.Empty);
        HudWorkbench actedBench = null;
        var benchAction = HudWorkActionKind.None;
        foreach (var bench in _benches)
        {
            var action = bench.Update(mouse, _previousMouse, pointer, state, workLocked);
            if (actedBench is null && action != HudWorkActionKind.None)
            {
                (actedBench, benchAction) = (bench, action);
            }
        }

        if (warehouseAction.Kind != HudWarehouseActionKind.None)
        {
            ApplyWarehouseAction(state, warehouseAction);
        }
        else if (actedBench is not null)
        {
            ApplyWorkAction(actedBench, state, benchAction);
        }
        else if (clicked)
        {
            Click(state, layout, pointer, design, keyboard);
        }

        // A right click on a crystal pet's icon in the running-effects bar asks whether to dismiss the pet.
        var rightPressed = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
        if (rightPressed && !ModalOpen && !_settingsOpen && _partyFrames.RightClick(layout, Party, SelfId, pointer))
        {
            rightPressed = false;
        }

        if (rightPressed && !ModalOpen && !_settingsOpen && _premium.CaravanAt(pointer) is { } caravanName)
        {
            _caravanDismiss.Offer(caravanName);
        }
        else if (rightPressed && !ModalOpen && !_settingsOpen)
        {
            _crystalDismiss.Offer(_skillHud.BuffAt(pointer));
        }
        else if (_crystalDismiss.Confirming && !_skillHud.Shows(_crystalDismiss.StatusCode))
        {
            // The pet went (expired, died, or its owner entered a town) while its question was open.
            _crystalDismiss.Close();
        }

        var skillOrigin = PanelOrigin(layout, _skillSlide);
        _skills.RefreshRanks(_state);
        var hoveredSkill = _skillSlide.FullyOpen && _skillMaster == 0 ? _skills.HoveredSkill(skillOrigin, pointer) : 0;
        _minimap.Drag(layout, pointer, mouse, _previousMouse, MinimapShown && !ModalOpen && !_settingsOpen && _inventory.Dragging == Guid.Empty
            && Storage.Dragging == Guid.Empty && _actionBar.Carried.IsEmpty && !_questTracker.Dragging);
        _questTracker.Update(layout, pointer, mouse, _previousMouse, !ModalOpen && !_settingsOpen && !_minimap.Dragging && _inventory.Dragging == Guid.Empty
            && Storage.Dragging == Guid.Empty && _actionBar.Carried.IsEmpty);

        _tuning.Update(layout, pointer, mouse, _previousMouse, _clock.Elapsed.TotalSeconds);
        _partyFrames.Drag(layout, Party, pointer, mouse, _previousMouse, !ModalOpen && !_settingsOpen && !_minimap.Dragging && !_questTracker.Dragging
            && _inventory.Dragging == Guid.Empty && Storage.Dragging == Guid.Empty && _actionBar.Carried.IsEmpty);
        _worldBuffs.Update(layout, pointer, mouse, _previousMouse, !ModalOpen && !_settingsOpen && !_minimap.Dragging && !_questTracker.Dragging
            && _inventory.Dragging == Guid.Empty && Storage.Dragging == Guid.Empty && _actionBar.Carried.IsEmpty);

        // The bar reads the skill window's press before the bindings forget it on its release.
        _actionBar.Update(layout, pointer, mouse, _previousMouse, _inventory.Dragging != Guid.Empty || Storage.Dragging != Guid.Empty,
            ModalOpen || _settingsOpen);
        _bindings.Update(typing ? default : keyboard, mouse, hoveredSkill);
        SaveActionBar();
        PointerCaptured = clickTaken || Captures(layout, pointer, design) || _chat.Window.Contains(layout, pointer, ChatYields(layout)) || _inventory.Dragging != Guid.Empty || Storage.Dragging != Guid.Empty;
        // A click in the world (to move or act) lets go of the chat's input line, keeping what was typed.
        if (clicked && !PointerCaptured && _chat.Window.Focused)
        {
            _chat.Window.Unfocus();
        }

        _inventory.PickupLocked = PlayerActing || storedCarried;
        _inventory.ActionStarts = PlayerActionStarts;
        _inventory.Update(state, layout, _inventorySlide.FullyOpen && !_settingsOpen && !ModalOpen, keyboard, mouse);
        _previousKeyboard = keyboard;
        _previousMouse = mouse;
    }

    // A status, skill or inventory window shrinks the chat window above it.
    private bool SideWindowOpen => _inventorySlide.Visible || _statusSlide.Visible || _skillSlide.Visible;

    // The chat shrinks out of a side window's way only where that window reaches it: with the bottom interface centred on
    // a screen wide enough, the windows open beside the chat and it stays put.
    private bool ChatYields(HudLayout layout) => SideWindowOpen && layout.CenterOffset < HudChatWindow.Box(layout, false).Right;

    private long QuestTimeLeftMs => _questView is null ? 0 : Math.Max(0, _questView.TimerMs - (long)(_clock.Elapsed.TotalMilliseconds - _questViewMs));

    // The pointer the HUD imposes, or null to let the scene choose: the arrow over a dialog or message box, the shop
    // mode's pointer while a shop is open, none while an item is dragged.
    public HudCursorKind? CursorOverride =>
        ModalOpen ? HudCursorKind.Default
        : _shop.IsOpen ? _shop.Mode switch { HudShopMode.Sell => HudCursorKind.Sell, HudShopMode.Repair => HudCursorKind.Repair, _ => HudCursorKind.Buy }
        : _inventory.Dragging != Guid.Empty || Storage.Dragging != Guid.Empty ? HudCursorKind.Hidden
        : null;

    // A dialog, a message box or the warp map: it takes every click, the bag's included.
    private bool ModalOpen =>
        _countBox.IsOpen || _personalShop.ModalOpen || _npcOpen || _clanWindow.IsOpen || _castleWindow.IsOpen || _bellatraWindow.IsOpen || _quests.Dialog is not null || _quests.Confirming || _quests.SkillResetBox || _skills.TrainingPending
        || _wingGate.IsOpen || _wingGate.Confirming || _itemUse.Teleport.IsOpen || _itemUse.Confirming || _puzzle.Confirming
        || _crystalDismiss.Confirming || _caravanDismiss.Confirming;

    // As in the original, a modal or a work window keeps every click from the world (the work windows still trade items
    // with the bag); the status, inventory, skill and shop windows keep only their own area.
    private bool BlocksWorld => ModalOpen || ServiceWindowOpen(blockingOnly: true);

    // As in the original, the player stands still while it deals with an NPC (its dialog, a service window, the skill
    // master) or a modal: a click into the world neither walks nor acts, and a walk in progress stops.
    public bool HoldsPlayer => ModalOpen || ServiceWindowOpen(blockingOnly: false) || _skillMaster != 0 || _personalShop.Selling;

    private bool MinimapShown => _mapVisible && !World.MapHidden;

    // Renders the target frame's 3D portrait into its own target. Call before the world and HUD draw of the frame: binding
    // another target discards the back buffer.
    public void RenderPortrait()
    {
        // The new target frame shows no portrait.
        var subject = TargetId != 0 && _context.Presentation.Requested.ClassicTargetFrame && Combat.TryGetPortraitSubject(TargetId, out var target)
            ? target
            : default;
        _portrait.Render(PortraitScene, subject, Layout());
    }

    public void Draw()
    {
        var layout = Layout();
        HudMessageBox.Place(layout);
        var pointer = layout.ToLogical(_previousMouse.Position);
        var pressed = _previousMouse.LeftButton == ButtonState.Pressed;
        _painter.Begin(layout);
        HudGroundLabels.DrawBeams(_painter, layout, GroundBeams, _clock.Elapsed.TotalMilliseconds);
        _dropNames.Draw(_painter, layout, GroundLabels, GroundLabelPlaced);
        Combat.ClockMs = _clock.Elapsed.TotalMilliseconds;
        Combat.DrawNamePlates(_painter, layout, Overhead, HoveredId, TracedId, _context.Presentation.Requested.ClassicNamePlates);
        DrawPetLabels(layout);
        _chat.Bubbles.Signs = ShopSigns;
        _chat.Bubbles.PlateTops = Combat.PlateTops;
        _chat.Bubbles.Visited = _personalShop.Visited;
        _chat.Bubbles.Draw(_painter, layout, _clock.Elapsed.TotalMilliseconds, Overhead, _nameColor ??= id => _chat.NameColor(id, TargetId), TargetId);
        if (_helpPet.Bubble is { } petLine && HelperPet.Point is { } petPoint)
        {
            _chat.Bubbles.DrawAt(_painter, layout, petLine, petPoint);
        }

        _painter.Flush();
        if (MinimapShown)
        {
            CollectMinimapMarkers();
            _minimap.Draw(_painter, layout, World, _minimapMarkers, _minimap.Area(layout).Contains(pointer));
            if (_bossTime.Text(_clock.Elapsed.TotalMilliseconds) is { Length: > 0 } bossLine)
            {
                _minimap.DrawBossTime(_painter, layout, bossLine);
            }

            _bossList.DrawButton(_painter, _minimap.Area(layout), pointer);
            HudPetWindow.DrawButton(_painter, _minimap.Area(layout), pointer, _petWindow.Open);
            HudQuestBook.DrawButton(_painter, _minimap.Area(layout), pointer, _questBook.Open);
            HudCoinShop.DrawButton(_painter, _minimap.Area(layout), pointer, _coinShop.Open);
            _painter.Flush();
            _bossList.Draw(_painter, layout, _minimap.Area(layout), _clock.Elapsed.TotalMilliseconds);
        }

        var classicFrame = _context.Presentation.Requested.ClassicTargetFrame;
        Combat.DrawTarget(_painter, layout, TargetId, _context.Presentation.Requested.ClassicTargetHealthBar, _portrait.Texture(TargetId),
            false, classicFrame);
        var menuPlayer = MenuPlayerId;
        if (menuPlayer != 0)
        {
            _targetMenu.Offer(menuPlayer, OffersRaid(menuPlayer), OffersClan(menuPlayer));
            _targetMenu.Draw(_painter, HudCombat.TargetFrame(layout, classicFrame), classicFrame, pointer);
        }
        else
        {
            _targetMenu.Close();
        }
        _worldBuffs.Draw(_painter, layout, _clock.Elapsed.TotalMilliseconds, pointer, _premium);
        _siegeBoard.Draw(_painter, layout, InCastleField, _clock.Elapsed.TotalMilliseconds, Combat.OwnClan);
        _bellatraBoard.Draw(_painter, layout);
        _personalShop.Draw(_painter);
        _chat.Window.Draw(_painter, layout, ChatYields(layout), pointer);

        if (_statusSlide.Visible)
        {
            _status.ClanName = Clan.Name;
            _status.Draw(_painter, PanelOrigin(layout, _statusSlide), _state, pointer, pressed, _statusSlide.FullyOpen);
        }

        if (_skillSlide.Visible)
        {
            _skills.Draw(_painter, PanelOrigin(layout, _skillSlide), pointer, _state, _bindings, _skillMaster, pressed, _skillSlide.FullyOpen,
                Cooldowns, SkillClockMs);
        }

        if (_inventorySlide.Visible)
        {
            _inventoryPanel.ClockMs = _clock.Elapsed.TotalMilliseconds;
            var bagOrigin = PanelOrigin(layout, _inventorySlide);
            _inventoryPanel.Draw(_painter, bagOrigin, _state, _inventory, _icons, pointer, _inventorySlide.FullyOpen, Storage.Carried);
            var money = Inventory.HudInventoryLayout.Money(bagOrigin);
            _painter.Text("Coins", money.Right + 40, money.Y, BagCoinColor);
            _painter.Text(Coins.Text, money.Right + 84, money.Y, BagCoinColor);
        }
        else
        {
            _inventoryPanel.TrackHeld(_state, _inventory, pointer);
        }

        if (_questWindow.Visible)
        {
            _questWindow.Draw(_painter, layout, _context.Font, _questView, _questDocuments, pointer);
        }

        _partyWindow.Draw(_painter, layout, Party, SelfId, pointer);
        _partyFrames.Draw(_painter, layout, Party, SelfId, pointer);
        _social.Draw(_painter, layout, pointer);
        if (MinimapShown)
        {
            _achievements.DrawButton(_painter, _minimap.Area(layout));
        }

        _achievements.Draw(_painter, layout, pointer);
        _rankings.Draw(_painter, layout, pointer);
        _partyFinder.Draw(_painter, layout, pointer);
        _courierWindow.Draw(_painter, layout, pointer, _clock.Elapsed.TotalMilliseconds);

        var incoming = OwnedItemView.Find(_state?.Items, _inventory.Dragging);
        var serviceFrame = new HudServiceFrame(_painter, layout, _icons, _state, pointer, incoming, _settingsOpen, _clock.Elapsed.TotalMilliseconds);
        foreach (var window in _serviceWindows)
        {
            if (window.Visible)
            {
                window.Draw(serviceFrame);
            }
        }

        _painter.Flush();
        _painter.Shift(layout.CenterOffset);
        var left = _bindings.LeftSkillId == HudSkillBindings.BasicAttackId;
        _bar.Draw(_painter, layout, _state, new HudBarView(World.TimeOfDay, World.Running, _mapVisible, left, RightAttackEnabled,
            _hoveredButton, pressed && _hoveredButton is not null, _hoveredToggle));
        if (AutoSkill)
        {
            var socket = HudBar.Socket(true);
            _painter.Stretch(AutoSkillArt, new Rectangle(socket.X - 6, socket.Y + layout.BottomOffset - 6, socket.Width + 12, socket.Height + 12));
        }

        _skillHud.DrawSockets(_painter, layout.BottomOffset, _bindings.LeftSkillId, _bindings.RightSkillId, Cooldowns, SkillClockMs);
        _painter.Shift(-layout.CenterOffset);
        _inventoryPanel.DrawBelt(_painter, HudInventoryLayout.Origin(layout), _state, _inventory, _icons);
        if (Statuses is not null && SelfId != 0)
        {
            _skillHud.DrawBuffs(_painter, _premiumKeeps.With(Statuses.For(SelfId), PremiumTimers, SkillClockMs), SkillClockMs);
        }
        else
        {
            _skillHud.ClearBuffs();
        }

        _skillHud.DrawQuestTimer(_painter, _questView, QuestTimeLeftMs);
        _actionBar.Draw(_painter, layout, _state, _icons, Cooldowns, SkillClockMs, _skillHud, pointer);

        StringEffect.Draw(_painter, layout);
        _painter.Shift(layout.CenterOffset);
        _bar.DrawHoverText(_painter, layout, _state, pointer);
        if (_systemMenu)
        {
            HudSystemMenu.Draw(_painter, layout, pointer);
        }

        _painter.Shift(-layout.CenterOffset);

        _painter.Flush();
        _helpPet.Draw(_painter, pointer);
        DrawHelp(pointer);

        var trainer = _skillSlide.FullyOpen ? _skillMaster : 0;
        _skills.DrawMasterBox(_painter, _state, trainer, trainer != 0 && _quests.ClassChangeNpc == trainer);

        // The quest dialog takes the help box from the NPC dialog; its yes/no box replaces both, as the classic client closed
        // the help box before asking.
        if (_quests.Dialog is not null)
        {
            if (!_quests.Confirming)
            {
                _painter.Flush();
                _questDialog.Draw(_painter, _context.Font, _quests, _questDocuments, pointer);
            }
        }
        else if (_clanWindow.IsOpen)
        {
            _painter.Flush();
            _clanWindow.Draw(_painter, _context.Font, pointer);
        }
        else if (_castleWindow.IsOpen)
        {
            _painter.Flush();
            _castleWindow.Draw(_painter, pointer);
        }
        else if (_bellatraWindow.IsOpen)
        {
            _painter.Flush();
            _bellatraWindow.Draw(_painter, pointer);
        }
        else if (_npcOpen)
        {
            _painter.Flush();
            _npc.Draw(_painter, pointer);
        }

        if (_bellatraResult.IsOpen)
        {
            _painter.Flush();
            _bellatraResult.Draw(_painter, pointer);
        }

        if (SiegeResultShown)
        {
            _painter.Flush();
            _siegeResult.Draw(_painter, pointer);
        }

        if (_itemUse.Teleport.IsOpen || _itemUse.Confirming)
        {
            _painter.Flush();
            _itemUse.Draw(_painter, pointer, _bossList, _clock.Elapsed.TotalMilliseconds);
        }

        if (_wingGate.IsOpen || _wingGate.Confirming)
        {
            _painter.Flush();
            _wingGate.Draw(_painter, _context.Font, _questDocuments, _state, pointer, _clock.Elapsed.TotalMilliseconds);
        }

        _puzzle.Draw(_painter, pointer);
        _crystalDismiss.Draw(_painter, pointer);
        _caravanDismiss.Draw(_painter, pointer);
        if (_personalShop.ModalOpen)
        {
            _painter.Flush();
            _personalShop.DrawModal(_painter, pointer);
        }

        if (_countBox.IsOpen)
        {
            _painter.Flush();
            _countBox.Draw(_painter, pointer);
        }

        if (_settingsOpen)
        {
            _painter.Flush();
            _settings.Draw(_painter, pointer);
        }

        foreach (var window in _serviceWindows)
        {
            if (window.FullyOpen)
            {
                window.DrawOverlay(serviceFrame);
            }
        }

        if (_inventorySlide.FullyOpen || _inventory.Dragging != Guid.Empty)
        {
            _inventoryPanel.DrawOverlay(_painter, layout, _icons, pointer);
        }

        _questBoxGlow.Draw(_painter, _clock.Elapsed.TotalMilliseconds);

        if (_skillSlide.FullyOpen)
        {
            _skills.DrawOverlay(_painter, layout, pointer);
        }

        if (Statuses is not null && SelfId != 0)
        {
            _skillHud.DrawBuffTooltip(_painter, _premiumKeeps.With(Statuses.For(SelfId), PremiumTimers, SkillClockMs), pointer, SkillClockMs,
                SummonAutomatic);
        }

        _skillHud.DrawQuestTimerTooltip(_painter, _questView, QuestTimeLeftMs, pointer);
        if (_quests.Confirming)
        {
            _painter.Flush();
            HudQuestDialog.DrawConfirm(_painter, _quests, pointer);
        }
        else if (_quests.SkillResetBox)
        {
            _painter.Flush();
            HudQuestDialog.DrawBox(_painter, ClientText.Format(QuestTexts.SkillPointName), ClientText.Format(QuestTexts.Redistribute.Item1),
                ClientText.Format(QuestTexts.Redistribute.Item2), pointer);
        }

        _painter.Flush();
        _tuning.Draw(_painter, layout, pointer);
        _partyFrames.DrawConfirm(_painter, pointer);
        _premium.DrawHover(_painter, pointer, _clock.Elapsed.TotalMilliseconds);
        _painter.Flush();
        _partyRequest.Draw(_painter, layout, pointer, _clock.Elapsed.TotalMilliseconds);
        _clanRequest.Draw(_painter, layout, pointer, _clock.Elapsed.TotalMilliseconds);
        _tradeRequest.Draw(_painter, layout, pointer, _clock.Elapsed.TotalMilliseconds);
        _restart.Draw(_painter, layout, pointer, RestartOffer?.FieldOpen == true, InCastleField);
        _painter.Flush();
        _petWindow.Draw(_painter, layout, PetCollection, CharacterName, _icons, pointer);
        _painter.Flush();
        _questTracker.Refresh(_painter, _questView, _weaponHunt);
        _questTracker.RefreshDailies(_painter, DailyQuests);
        _questTracker.Visible = !_questBook.Open;
        _questTracker.Draw(_painter, layout, pointer);
        _questBook.Draw(_painter, layout, pointer);
        _painter.Flush();
        _coinShop.Draw(_painter, layout, _icons, pointer);
        _painter.Flush();
        _itemBox.Draw(_painter, layout, _icons, _state, pointer);
        if (World is { } world)
        {
            _arenaTimer.Draw(_painter, layout, Combat.FieldCode, world.PlayerPosition);
        }

        _arenaEvent.Draw(_painter, layout, pointer);
        _cabinet.Draw(_painter, layout, _icons, pointer);
        _characterMarket.Draw(_painter, layout, _icons, pointer);
        _marketplace.Draw(_painter, layout, _icons, _state, pointer);
        _painter.Flush();
        DrawMinimapName(layout, pointer);
        _actionBar.DrawOverlay(_painter, layout, _state, _icons, _skillHud, pointer);
        DrawChatLink(layout, pointer);
        _painter.Flush();
        _notice.Draw(_painter, layout);
        _painter.End();
        DrawBellatraShow();
    }

    // Each pet's name in the NPCs' name box over it, within the distance speech bubbles show at.
    private void DrawPetLabels(HudLayout layout)
    {
        var scale = Math.Max(1, layout.Scale);
        foreach (var (point, text) in PetLabels)
        {
            if (point.Depth <= HudChatBubbles.MaxDepth)
            {
                var x = (int)(point.Screen.X / scale);
                HudCombat.DrawNamePlate(_painter, layout, text, x, (int)(point.Screen.Y / scale), PetLabelColor);
            }
        }

        foreach (var (point, text) in CaravanLabels)
        {
            if (point.Depth <= HudChatBubbles.MaxDepth)
            {
                HudCombat.DrawNamePlate(_painter, layout, text, (int)(point.Screen.X / scale), (int)(point.Screen.Y / scale), CaravanLabelColor);
            }
        }
    }

    private static readonly Color CaravanLabelColor = new(255, 214, 120);
    private const int CaravanClickWidth = 120;
    private const int CaravanClickHeight = 44;

    // The area over the local player's caravan whose click opens its storage.
    private Rectangle? OwnCaravanArea(HudLayout layout)
    {
        var scale = Math.Max(1, layout.Scale);
        return OwnCaravan is { } point && point.Depth <= HudChatBubbles.MaxDepth
            ? new Rectangle((int)(point.Screen.X / scale) - CaravanClickWidth / 2, (int)(point.Screen.Y / scale) - CaravanClickHeight, CaravanClickWidth, CaravanClickHeight + 8)
            : null;
    }

    private static readonly Color PetLabelColor = new(180, 240, 180);

    // An item shortcut's key: one of the item is used as a right click on it would.
    private void UseShortcutItem(string catalogKey)
    {
        if (!_inventory.UseByKey(catalogKey))
        {
            _notice.Show("None of that item is carried.");
        }
    }

    private void RestoreActionBar()
    {
        if (!_actionBarStore.Loaded(out var saved))
        {
            return;
        }

        if (saved is not null)
        {
            _actionBar.Restore(saved.Open, saved.Locked, saved.Vertical, new Point(saved.X, saved.Y));
            _bindings.Slots.Restore(saved.Slots, saved.Page);
            _minimap.Offset = new Point(saved.MapX, saved.MapY);
            _questTracker.Offset = new Point(saved.TrackerX, saved.TrackerY);
            _questTracker.Collapsed = saved.TrackerCollapsed;
            _worldBuffs.Offset = new Point(saved.BuffsX, saved.BuffsY);
            _partyFrames.Offset = new Point(saved.PartyX, saved.PartyY);
            _worldBuffs.Collapsed = saved.BuffsCollapsed;
        }

        _savedSlots = _bindings.Slots.Revision;
        _savedBar = _actionBar.Revision;
        _savedMap = _minimap.Revision;
        _savedTracker = _questTracker.Revision;
        _savedBuffs = _worldBuffs.Revision;
        _savedParty = _partyFrames.Revision;
    }

    // Saves the bar when a shortcut, the page or the window changed (not while the character's file is still read).
    private void SaveActionBar()
    {
        if (_actionBarStore.Reading || _savedSlots == _bindings.Slots.Revision && _savedBar == _actionBar.Revision && _savedMap == _minimap.Revision
            && _savedTracker == _questTracker.Revision && _savedBuffs == _worldBuffs.Revision && _savedParty == _partyFrames.Revision)
        {
            return;
        }

        _savedSlots = _bindings.Slots.Revision;
        _savedBar = _actionBar.Revision;
        _savedMap = _minimap.Revision;
        _savedTracker = _questTracker.Revision;
        _savedBuffs = _worldBuffs.Revision;
        _savedParty = _partyFrames.Revision;
        _actionBarStore.Save(new HudActionBarDocument(HudActionBarDocument.CurrentVersion, _actionBar.Open, _actionBar.Locked, _actionBar.Vertical,
            _actionBar.Anchor.X, _actionBar.Anchor.Y, _bindings.Slots.Page, _bindings.Slots.Save(), _minimap.Offset.X, _minimap.Offset.Y)
        {
            TrackerX = _questTracker.Offset.X,
            TrackerY = _questTracker.Offset.Y,
            TrackerCollapsed = _questTracker.Collapsed,
            BuffsX = _worldBuffs.Offset.X,
            PartyX = _partyFrames.Offset.X,
            PartyY = _partyFrames.Offset.Y,
            BuffsY = _worldBuffs.Offset.Y,
            BuffsCollapsed = _worldBuffs.Collapsed
        });
    }

    // The tooltip of an item linked in the chat line under the pointer.
    private void DrawChatLink(HudLayout layout, Point pointer)
    {
        if (_chat.Window.HoveredItem is not { } item || _state is null)
        {
            return;
        }

        if (!ReferenceEquals(item, _chatLinkShown))
        {
            (_chatLinkShown, _chatLinkLines) = (item, HudItemDetailTooltip.Lines(item, _state, "", ""));
        }

        HudItemDetailTooltip.Draw(_painter, layout, pointer, _chatLinkLines, null, HudItemDetailTooltip.RarityOf(item));
    }

    private OwnedItemView _chatLinkShown;
    private List<(string Text, Color Color)> _chatLinkLines;

    private HudLayout Layout() => HudLayout.Of(_context.GraphicsDevice.Viewport, _context.Presentation.Requested.CenteredInventory);

    private static Point PanelOrigin(HudLayout layout, HudSlide slide) => HudInventoryLayout.Origin(layout, slide.Offset);

    private static bool WindowContains(HudLayout layout, HudSlide slide, Point pointer) =>
        slide.Visible && HudInventoryLayout.Window(PanelOrigin(layout, slide)).Contains(pointer);

    public void Dispose()
    {
        _portrait.Dispose();
        _skillHud.Dispose();
        _icons.Dispose();
        _clanWindow.Dispose();
        _painter.Dispose();
        _sprites.Dispose();
    }
}
