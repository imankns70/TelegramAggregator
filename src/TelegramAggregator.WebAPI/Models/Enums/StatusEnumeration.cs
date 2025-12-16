public class StatusEnumeration : Enumeration
{
    public static readonly StatusEnumeration Receive = new StatusEnumeration(1, "دریافت");
    public static readonly StatusEnumeration Decay = new StatusEnumeration(2, "رد");
    public static readonly StatusEnumeration InQueue = new StatusEnumeration(3, "در صف");
    public static readonly StatusEnumeration Sent = new StatusEnumeration(4, "ارسال شد");
    public StatusEnumeration(int id, string name) : base(id, name)
    {        
    }
}