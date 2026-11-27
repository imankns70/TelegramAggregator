using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TelegramAggregator.WebAPI.Services.LiveBoard;

internal static class PersianTeamNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aluminium"] = "آلومینیوم اراک",
        ["Aluminium Arak"] = "آلومینیوم اراک",
        ["Chadormalu Ardakan SC"] = "چادرملو اردکان",
        ["Esteghlal"] = "استقلال",
        ["Esteghlal Kh"] = "استقلال خوزستان",
        ["Esteghlal Khuzestan"] = "استقلال خوزستان",
        ["Fajr Sepasi"] = "فجر سپاسی",
        ["Foolad"] = "فولاد خوزستان",
        ["Foolad Khuzestan"] = "فولاد خوزستان",
        ["Gol Gohar"] = "گل‌گهر سیرجان",
        ["Kheybar"] = "خیبر خرم‌آباد",
        ["Kheybar Khorramabad"] = "خیبر خرم‌آباد",
        ["Malavan"] = "ملوان",
        ["Mes Shahr Babak"] = "مس شهربابک",
        ["Mes Rafsanjan"] = "مس رفسنجان",
        ["Nassaji"] = "نساجی مازندران",
        ["Paykan"] = "پیکان",
        ["Persepolis"] = "پرسپولیس",
        ["Sanat-Naft"] = "صنعت نفت آبادان",
        ["Sepahan"] = "سپاهان",
        ["Shams Azar"] = "شمس‌آذر قزوین",
        ["Shams Azar Qazvin"] = "شمس‌آذر قزوین",
        ["Tractor"] = "تراکتور",
        ["Zob Ahan"] = "ذوب‌آهن",
        ["Arsenal"] = "آرسنال",
        ["Aston Villa"] = "استون ویلا",
        ["Bournemouth"] = "بورنموث",
        ["Brentford"] = "برنتفورد",
        ["Brighton"] = "برایتون",
        ["Chelsea"] = "چلسی",
        ["Crystal Palace"] = "کریستال پالاس",
        ["Everton"] = "اورتون",
        ["Fulham"] = "فولام",
        ["Leeds United"] = "لیدز یونایتد",
        ["Liverpool"] = "لیورپول",
        ["Man City"] = "منچسترسیتی",
        ["Manchester City"] = "منچسترسیتی",
        ["Man United"] = "منچستریونایتد",
        ["Manchester United"] = "منچستریونایتد",
        ["Newcastle United"] = "نیوکاسل",
        ["Nottingham Forest"] = "ناتینگهام فارست",
        ["Sunderland"] = "ساندرلند",
        ["Tottenham Hotspur"] = "تاتنهام",
        ["West Ham United"] = "وستهام",
        ["Wolverhampton Wanderers"] = "ولورهمپتون",
        ["Atletico Madrid"] = "اتلتیکو مادرید",
        ["Athletic Club"] = "اتلتیک بیلبائو",
        ["Barcelona"] = "بارسلونا",
        ["Celta Vigo"] = "سلتاویگو",
        ["Espanyol"] = "اسپانیول",
        ["Getafe"] = "ختافه",
        ["Girona"] = "خیرونا",
        ["Levante"] = "لوانته",
        ["Osasuna"] = "اوساسونا",
        ["Racing Santander"] = "راسینگ سانتاندر",
        ["Real Betis"] = "رئال بتیس",
        ["Real Madrid"] = "رئال مادرید",
        ["Real Sociedad"] = "رئال سوسیداد",
        ["Sevilla"] = "سویا",
        ["Valencia"] = "والنسیا",
        ["Villarreal"] = "ویارئال",
        ["Atalanta"] = "آتالانتا",
        ["Bologna"] = "بولونیا",
        ["Como"] = "کومو",
        ["Fiorentina"] = "فیورنتینا",
        ["Genoa"] = "جنوا",
        ["Inter"] = "اینتر",
        ["Juventus"] = "یوونتوس",
        ["Lazio"] = "لاتزیو",
        ["Milan"] = "میلان",
        ["Napoli"] = "ناپولی",
        ["Roma"] = "رم",
        ["Torino"] = "تورینو",
        ["Udinese"] = "اودینزه",
        ["Venezia"] = "ونتزیا",
        ["Bayern Munchen"] = "بایرن مونیخ",
        ["Borussia Dortmund"] = "بوروسیا دورتموند",
        ["Dortmund"] = "دورتموند",
        ["Borussia Monchengladbach"] = "بوروسیا مونشن‌گلادباخ",
        ["Borussia M'gladbach"] = "بوروسیا مونشن‌گلادباخ",
        ["Bayer Leverkusen"] = "بایر لورکوزن",
        ["Eintracht Frankfurt"] = "آینتراخت فرانکفورت",
        ["RB Leipzig"] = "لایپزیگ",
        ["VfB Stuttgart"] = "اشتوتگارت",
        ["Werder Bremen"] = "وردر برمن",
        ["Brest"] = "برست",
        ["Lens"] = "لانس",
        ["Lille"] = "لیل",
        ["Lyon"] = "لیون",
        ["Marseille"] = "مارسی",
        ["Monaco"] = "موناکو",
        ["Nice"] = "نیس",
        ["Paris FC"] = "پاریس اف‌سی",
        ["Paris Saint-Germain"] = "پاری‌سن‌ژرمن",
        ["PSG"] = "پاری‌سن‌ژرمن",
        ["Rennes"] = "رن",
        ["Al Ain"] = "العین",
        ["Al-Ain"] = "العین",
        ["Al Ahli"] = "الاهلی",
        ["Al Hilal"] = "الهلال",
        ["Al Ittihad"] = "الاتحاد",
        ["Al Nassr"] = "النصر",
        ["Al Nassr FC"] = "النصر",
        ["Al Sadd"] = "السد",
        ["Al-Sadd"] = "السد",
        ["Al Wasl"] = "الوصل",
        ["Al-Wasl"] = "الوصل",
        ["Al Jazira"] = "الجزیره",
        ["Al-Jazira"] = "الجزیره",
        ["Al Taawoun"] = "التعاون",
        ["Al-Rayyan"] = "الریان",
        ["Al-Gharafa"] = "الغرافه",
        ["Pakhtakor"] = "پاختاکور",
        ["Galatasaray"] = "گالاتاسرای",
        ["Fenerbahce"] = "فنرباغچه",
        ["Besiktas"] = "بشیکتاش",
        ["Iran"] = "ایران",
        ["England"] = "انگلیس",
        ["France"] = "فرانسه",
        ["Germany"] = "آلمان",
        ["Italy"] = "ایتالیا",
        ["Spain"] = "اسپانیا",
        ["Portugal"] = "پرتغال",
        ["Netherlands"] = "هلند",
        ["Saudi Arabia"] = "عربستان سعودی",
        ["United Arab Emirates"] = "امارات",
        ["Qatar"] = "قطر",
        ["Iraq"] = "عراق",
        ["Oman"] = "عمان",
        ["Uzbekistan"] = "ازبکستان",
        ["Australia"] = "استرالیا",
        ["China"] = "چین",
        ["South Korea"] = "کرهٔ جنوبی",
        ["Japan"] = "ژاپن",
        ["United States"] = "آمریکا",
        ["USA"] = "آمریکا",
        ["Argentina"] = "آرژانتین",
        ["Brazil"] = "برزیل"
    };

    private static readonly Regex LatinWords = new("[A-Za-z]+", RegexOptions.CultureInvariant);
    private static readonly Regex LatinSounds = new("sch|tch|ch|sh|zh|kh|gh|ph|th|oo|ee|ou|ai|ay|ei|[a-z]", RegexOptions.CultureInvariant);
    private static readonly string[] LatinLetters =
        { "ا", "ب", "ک", "د", "ه", "ف", "گ", "ه", "ی", "ج", "ک", "ل", "م", "ن", "و", "پ", "ق", "ر", "س", "ت", "و", "و", "و", "کس", "ی", "ز" };

    public static string Translate(string name)
    {
        var normalized = WithoutLatinAccents(name.Trim()).Replace('ي', 'ی').Replace('ك', 'ک').Replace('ى', 'ی');
        return Names.TryGetValue(normalized, out var known)
            ? known
            : LatinWords.Replace(normalized, word => LatinSounds.Replace(word.Value.ToLowerInvariant(), sound => sound.Value switch
            {
                "sch" or "sh" => "ش",
                "tch" or "ch" => "چ",
                "zh" => "ژ",
                "kh" => "خ",
                "gh" => "غ",
                "ph" => "ف",
                "th" => "ت",
                "oo" or "ou" => "و",
                "ee" => "ی",
                "ai" or "ay" or "ei" => "ای",
                _ => LatinLetters[sound.Value[0] - 'a']
            }));
    }

    private static string WithoutLatinAccents(string text)
    {
        if (!LatinWords.IsMatch(text) && !text.Any(c => c is >= '\u00C0' and <= '\u024F'))
            return text;

        return string.Concat(text.Replace("ß", "ss", StringComparison.OrdinalIgnoreCase)
            .Replace("ø", "o", StringComparison.OrdinalIgnoreCase).Replace("ł", "l", StringComparison.OrdinalIgnoreCase)
            .Replace("æ", "ae", StringComparison.OrdinalIgnoreCase).Replace("œ", "oe", StringComparison.OrdinalIgnoreCase)
            .Replace("ı", "i").Replace("đ", "d", StringComparison.OrdinalIgnoreCase)
            .Replace("ð", "d", StringComparison.OrdinalIgnoreCase).Replace("þ", "th", StringComparison.OrdinalIgnoreCase)
            .Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            .Normalize(NormalizationForm.FormC);
    }
}
