namespace renting_room.Domain.Contracts;

public enum CustomFieldType
{
    Text,      // ≤ 500 ký tự
    LongText,  // ≤ 5000 ký tự
    Number,
    Money,     // VND, số nguyên
    Date,      // yyyy-MM-dd
    Boolean,
    Select     // một giá trị trong Options
}
