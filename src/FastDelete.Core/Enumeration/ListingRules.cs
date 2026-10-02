namespace FastDelete.Core.Enumeration;

/// <summary>Which names appear in the list. Hidden and dot folders stay out unless asked for.</summary>
public static class ListingRules
{
    public static bool ShouldShow(string name, bool hiddenAttribute, bool systemAttribute, bool includeHidden)
    {
        if (string.IsNullOrEmpty(name) || name is "." or "..")
            return false;
        if (includeHidden)
            return true;
        if (hiddenAttribute || systemAttribute)
            return false;
        if (name[0] == '.')
            return false;
        return true;
    }
}
