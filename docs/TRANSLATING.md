# Translating Sonora

Sonora speaks **English, French, Spanish, German, Italian, Portuguese, Dutch, Swedish, Danish, Norwegian (Bokmål), Finnish and Icelandic**. Every language is one plain JSON file, so improving a translation is a small pull request, and you don't need to build anything to do it.

The files live in [`Sonora.Appearance/Localization/Languages/`](../Sonora.Appearance/Localization/Languages): `en.json`, `fr.json`, `es.json`…

## "Not reviewed" badges

Settings › Language marks a language with a small **not reviewed** badge until a native speaker has read the whole file. French is reviewed; the others were drafted and haven't been checked yet, so **they surely contain stiff phrasing or mistakes**. If you speak one of them, a read-through is the most useful contribution you can make.

When you have read a whole file and fixed what needed fixing, set `"reviewed": true` in it (in the same pull request) and the badge goes away.

## Fixing a translation

1. Open the language's file, find the text (search for the words you saw in the app), and edit it.
2. Keep every `{0}`, `{1}`… exactly as they are: they're filled in by the app (a name, a number, a time). You may move them within the sentence.
3. Open a pull request. The tests check that your file is complete and consistent (see below).

## The file format

```json
{
  "language": "fr",
  "name": "Français",
  "culture": "fr-FR",
  "reviewed": true,
  "strings": {
    "nav.home": "Accueil",
    "spotify.connected_as": "Connecté en tant que {0}",
    "count.tracks": { "one": "{0} titre", "other": "{0} titres" }
  }
}
```

- `name` is the language's name **in itself** ("Deutsch", not "German"); it is what the language list shows.
- `culture` decides how numbers and dates are written (`de-DE`, `nb-NO`…).
- A text is a string. A **counted** text (one that says "1 track" / "2 tracks") is an object with a `one` and an `other` form; the app picks the right one for the number. Sonora's languages need only these two forms. In French and Portuguese, 0 counts as *one*; in Icelandic, numbers ending in 1 (except 11) do.
- Keys use letters, digits, `.`, `_` and `-`.

## Adding a new language

1. Copy `en.json` to `<code>.json` (the two-letter ISO 639-1 code: `pl`, `ro`…), translate it, and set `language`, `name` and `culture`. Leave `"reviewed": false`.
2. Add the code to `LanguageCatalog.Codes` in `Sonora.Appearance/Localization/LanguageCatalog.cs`. The file is embedded automatically.
3. Add a plural rule to `PluralRules.cs` **only if** your language doesn't fit "one if the number is 1, otherwise other" (the [CLDR](https://cldr.unicode.org/index/cldr-spec/plural-rules) rules say which).
4. Run the tests (`dotnet test`). They fail with the exact keys that are missing or wrong.

Sonora's fonts (Segoe UI from Windows, plus the ones bundled with some themes) cover the Latin alphabet with the accents of the languages above. A language that needs other characters (Greek, Cyrillic, Arabic, CJK, or Latin letters such as the Polish ł) may need a font that has them, so ask in an issue first.

## For developers: adding text to the app

- **XAML:** `Text="{loc:T nav.home}"` (the `loc` namespace is `clr-namespace:Sonora.Localization`).
- **C#:** `Loc.T("key")`, `Loc.T("key", arg0, arg1)` for `{0}`, `{1}`, and `Loc.Count("count.tracks", n)` for counted texts.
- Add the key to **`en.json`** (the reference) and to every other file. A language missing a key shows the English text instead, never an empty label, but the tests require every file to have every key.
- Never use a displayed text as an identifier (a setting, a dictionary key, a comparison). Keep an internal id and translate it for display.
- Changing the language switches the interface live: anything you build once from texts (rather than binding to them) must be rebuilt in `MainViewModel.OnLanguageChanged`.

## What the tests check

`LocalizationTests` fails if a language file has a key English doesn't (or lacks one), loses or changes a `{0}`, has an empty text, forgets a plural form, uses a key with unsupported characters, or if the XAML or C# uses a key that `en.json` doesn't define.
