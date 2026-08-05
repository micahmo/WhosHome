namespace WhosHome.Server.Auth;

public static class AuthSchemes
{
    /// <summary>A household member, identified by the person they signed in as.</summary>
    public const string Member = "Member";

    /// <summary>Admin mode for a browser. Deliberately separate from <see cref="Member"/>:
    /// admin is a role held by a machine doing provisioning, not by a tracked person, so the
    /// laptop used for setup never has to exist on the board.</summary>
    public const string Admin = "Admin";

    public const string AdminTokenHeader = "X-WhosHome-Admin-Token";
}
