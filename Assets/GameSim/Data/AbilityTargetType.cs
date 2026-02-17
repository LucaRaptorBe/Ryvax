namespace MOBANet.GameSim.Data
{
    /// <summary>
    /// How the player aims and casts the ability.
    /// </summary>
    public enum AbilityTargetType
    {
        Targeted,   // Click on an entity (enemy/ally)
        Self,       // No targeting, applies to self
        GroundAOE,  // Click on ground, area of effect
        Skillshot,  // Straight line in a direction
        MeleeCone   // Cone in front of player
    }
}
