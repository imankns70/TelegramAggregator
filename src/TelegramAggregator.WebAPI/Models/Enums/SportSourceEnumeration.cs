public class SportSourceEnumeration : Enumeration
{
    public static readonly SportSourceEnumeration Varzesh3 = new SportSourceEnumeration(1, "ورزش سه | آخرین اخبار ورزشی");
    public static readonly SportSourceEnumeration Tarafdari = new SportSourceEnumeration(2, "طرفداری");
    public static readonly SportSourceEnumeration KhabarVarzeshi = new SportSourceEnumeration(3, "خبرورزشی؛ آخرین اخبار ورزشی ایران و جهان");
    public static readonly SportSourceEnumeration Football360 = new SportSourceEnumeration(4, "فوتبال 360");
    public SportSourceEnumeration(int id, string name) : base(id, name)
    {
    }
}


