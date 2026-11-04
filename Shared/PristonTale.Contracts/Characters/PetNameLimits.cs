namespace PristonTale.Contracts.Characters;

// How long a pet's name may be: MaxLength characters, kept in at most MaxBytes of UTF-8 on the wire. A name holds only
// letters, digits and spaces of the basic plane (PetNames), each three UTF-8 bytes at most.
public static class PetNameLimits
{
    public const int MaxLength = 16;
    public const int MaxBytes = MaxLength * 3;
}
