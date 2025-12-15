public class StatusEnumeration : Enumeration
{
    public static readonly StatusEnumeration Receive = new StatusEnumeration(1, "دریافت");
    public static readonly StatusEnumeration Decay = new StatusEnumeration(2, "رد");
    public static readonly StatusEnumeration Accepted = new StatusEnumeration(3, "قبول شد");
    public static readonly StatusEnumeration Sent = new StatusEnumeration(4, "ارسال شد");
    public StatusEnumeration(int id, string name) : base(id, name)
    {        
    }
}