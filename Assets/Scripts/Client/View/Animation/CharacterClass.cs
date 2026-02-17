namespace MOBANet.Client.Animation
{
    /// <summary>
    /// Character class types available in the game.
    /// Values must match SimPlayer.ClassId for proper synchronization.
    /// </summary>
    public enum CharacterClassType
    {
        None = 0,
        Archer = 1,
        Mage = 2,
        Fighter = 3,
        Assassin = 4,
        Tank = 5,
        Healer = 6,
        Summoner = 7,
        Warrior = 8
    }
}
