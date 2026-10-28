using System.Collections.Generic;

namespace CompetitiveMod
{
    // The locked competitive ruleset. Values are the raw PlayerPrefs the game stores (option rows store
    // the selected index, not the displayed value).
    internal static class Rules
    {
        // Option rows (PlayerPrefs key = the row's name). Auto Pickup and Chat are intentionally not here.
        internal static readonly Dictionary<string, int> Options = new Dictionary<string, int>
        {
            { "Maps", 0 },
            { "HP", 0 },           // 100 HP
            { "Regen", 0 },
            { "weaponSpawns", 1 },
            { "Bots", 0 },
            { "Ping", 0 },
            { "Show Wins", 1 },
        };

        // Map selection screen (which maps/categories are on, random order).
        internal const string SavedMaps = @"{""RandomOrderedMaps"":true,""SaveableCategories"":[{""CategoryName"":""Woods"",""CategoryActive"":true,""Actives"":[true,true,true,true],""Indexes"":[""1"",""2"",""3"",""4""]},{""CategoryName"":""Desert"",""CategoryActive"":true,""Actives"":[true,false,true,true,true,true,false,false,false,true],""Indexes"":[""5"",""6"",""7"",""8"",""9"",""77"",""11"",""12"",""75"",""76""]},{""CategoryName"":""Factory"",""CategoryActive"":true,""Actives"":[false,true,false,false,false,true,true,true,false,false,true,false],""Indexes"":[""13"",""14"",""15"",""16"",""17"",""18"",""19"",""20"",""21"",""70"",""71"",""72""]},{""CategoryName"":""Castle"",""CategoryActive"":true,""Actives"":[true,true,true,true,true,false,false,true,false,false,true,true,false,true,false,true,false,true],""Indexes"":[""22"",""23"",""24"",""25"",""26"",""27"",""28"",""29"",""30"",""31"",""32"",""33"",""34"",""35"",""36"",""37"",""38"",""67""]},{""CategoryName"":""Winter"",""CategoryActive"":true,""Actives"":[false,true,true,true,true,true,true,false,false,true,true,true,false,true,false,true],""Indexes"":[""47"",""48"",""49"",""50"",""51"",""52"",""53"",""54"",""55"",""56"",""57"",""58"",""69"",""78"",""79"",""80""]},{""CategoryName"":""Laser"",""CategoryActive"":true,""Actives"":[false,true,true,true,true,false,false,false,false],""Indexes"":[""59"",""60"",""61"",""62"",""63"",""64"",""65"",""66"",""68""]},{""CategoryName"":""Western"",""CategoryActive"":false,""Actives"":[true,true,true,true,true,true,true,true,true,true],""Indexes"":[""39"",""40"",""41"",""42"",""43"",""44"",""45"",""46"",""73"",""74""]},{""CategoryName"":""Lava"",""CategoryActive"":true,""Actives"":[true,true,false,true,false,false,false,true,false,true],""Indexes"":[""81"",""82"",""83"",""84"",""85"",""86"",""87"",""88"",""89"",""90""]},{""CategoryName"":""Xmas"",""CategoryActive"":true,""Actives"":[true,true,false,true,true,false,true,false,false,true,true,false,true,true,true,true,true,true,true,true],""Indexes"":[""124"",""105"",""106"",""107"",""108"",""109"",""110"",""111"",""112"",""113"",""114"",""115"",""116"",""117"",""118"",""119"",""120"",""121"",""122"",""123""]},{""CategoryName"":""Halloween"",""CategoryActive"":true,""Actives"":[true,false,false,true,false,true,true,false,true,false,false],""Indexes"":[""91"",""92"",""93"",""94"",""95"",""96"",""97"",""98"",""99"",""100"",""101""]},{""CategoryName"":"""",""CategoryActive"":false,""Actives"":[],""Indexes"":[]},{""CategoryName"":""CustomOnline"",""CategoryActive"":false,""Actives"":[true,true],""Indexes"":[""3719836926"",""3721741601""]}]}";

        // Weapon selection screen (which weapons/categories are on).
        internal const string SavedWeapons = @"{""Actives"":[true,true,true,true,false,true,true,true,false,false,true,false,false,false,false,false,true,false,false,false,false,false,false,false,true,true,true,false,false,false,false,false,true,true,false,false,false,true,false,false,true,false,false,false,false,false,false,false,false,false,false,false,false,false,false,false,false,false,false,false,false,true,true,true,true,true,true,true,false,true,true,true]}";
    }
}
