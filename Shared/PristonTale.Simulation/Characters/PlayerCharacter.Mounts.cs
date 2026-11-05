namespace PristonTale.Simulation.Characters;

// The account's mounts (a bit per Mounts id, joined from every character of the account), the one this character picked
// and whether it rides it now. Riding is not saved: a character enters the world on foot.
public sealed partial class PlayerCharacter
{
    public ulong Mounts { get; private set; }
    public int MountChosen { get; private set; }
    public bool Riding { get; private set; }

    // The mount drawn under the character: the chosen one while riding, else 0.
    public int MountRidden => Riding ? MountChosen : 0;

    // The move speed points riding adds now.
    private int MountSpeed => Characters.Mounts.Get(MountRidden) is { } mount ? mount.Speed : 0;

    public void RestoreMounts(ulong owned, int chosen)
    {
        Mounts = owned;
        MountChosen = Characters.Mounts.Has(owned, chosen) ? chosen : 0;
    }

    public void JoinAccountMounts(ulong owned) => Mounts |= owned;

    // Adds a mount to the stable and picks it; false for an unknown mount or one already owned.
    public bool UnlockMount(int id)
    {
        if (Characters.Mounts.Get(id) is null || Characters.Mounts.Has(Mounts, id))
        {
            return false;
        }

        Mounts |= Characters.Mounts.Bit(id);
        MountChosen = id;
        _derivedStale = true;
        return true;
    }

    // Picks an owned mount (riding on, when riding); false for a mount the account does not own.
    public bool ChooseMount(int id)
    {
        if (!Characters.Mounts.Has(Mounts, id))
        {
            return false;
        }

        MountChosen = id;
        _derivedStale = true;
        return true;
    }

    // Gets on the chosen mount or off it; false when there is no mount to ride or the character is dead.
    public bool Ride(bool riding)
    {
        if (riding && (MountChosen == 0 || Life <= 0))
        {
            return false;
        }

        if (Riding != riding)
        {
            Riding = riding;
            _derivedStale = true;
        }

        return true;
    }
}
