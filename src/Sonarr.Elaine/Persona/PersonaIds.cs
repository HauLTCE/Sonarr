namespace Sonarr.Elaine.Persona;

/// <summary>Where a persona element came from — every validation issue carries one.</summary>
/// <param name="File">Path relative to the persona root, e.g. <c>intents/social.yaml</c>.</param>
/// <param name="Path">Dotted location inside the file, e.g. <c>intents[3].match.regex[0]</c>.</param>
public readonly record struct PersonaLocation(string File, string Path)
{
    public override string ToString() => string.IsNullOrEmpty(Path) ? File : $"{File}:{Path}";
}

/// <summary>One raw persona file: relative path plus its text. The loader sees only these.</summary>
public readonly record struct PersonaFile(string RelativePath, string Text);
