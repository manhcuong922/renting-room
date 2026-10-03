using renting_room.Domain.Common;

namespace renting_room.Domain.Properties;

public static class PropertyErrors
{
    public static readonly Error PropertyNotFound = Error.NotFound("PROPERTY_NOT_FOUND", "Không tìm thấy khu trọ.");
    public static readonly Error PropertyCodeTaken = Error.Conflict("PROPERTY_CODE_TAKEN", "Mã khu trọ đã tồn tại.");
    public static readonly Error PropertyArchived = Error.BusinessRule("PROPERTY_ARCHIVED", "Khu trọ đã ngừng sử dụng.");
    public static readonly Error PropertyNotArchived = Error.Conflict("PROPERTY_NOT_ARCHIVED", "Khu trọ đang hoạt động.");
    public static readonly Error PropertyHasActiveContracts = Error.BusinessRule("PROPERTY_HAS_ACTIVE_CONTRACTS",
        "Khu trọ còn hợp đồng nháp / đang hiệu lực / đang thanh lý.");

    public static readonly Error RoomNotFound = Error.NotFound("ROOM_NOT_FOUND", "Không tìm thấy phòng.");
    public static readonly Error RoomCodeTaken = Error.Conflict("ROOM_CODE_TAKEN", "Mã phòng đã tồn tại trong khu.");
    public static readonly Error RoomArchived = Error.BusinessRule("ROOM_ARCHIVED", "Phòng đã ngừng sử dụng.");
    public static readonly Error RoomNotArchived = Error.Conflict("ROOM_NOT_ARCHIVED", "Phòng đang hoạt động.");
    public static readonly Error RoomOccupied = Error.BusinessRule("ROOM_OCCUPIED", "Phòng đang có hợp đồng hiệu lực.");
    public static readonly Error RoomHasContracts = Error.BusinessRule("ROOM_HAS_CONTRACTS",
        "Phòng còn hợp đồng nháp / đang hiệu lực / đang thanh lý.");
    public static readonly Error RoomAlreadyUnderMaintenance = Error.Conflict("ROOM_ALREADY_UNDER_MAINTENANCE", "Phòng đang bảo trì.");
    public static readonly Error RoomNotUnderMaintenance = Error.Conflict("ROOM_NOT_UNDER_MAINTENANCE", "Phòng không ở trạng thái bảo trì.");
    public static readonly Error MaxOccupantsBelowCurrent = Error.BusinessRule("MAX_OCCUPANTS_BELOW_CURRENT",
        "Sức chứa mới nhỏ hơn số người đang ở.");

    public static readonly Error RoomGroupNotFound = Error.NotFound("ROOM_GROUP_NOT_FOUND", "Không tìm thấy nhóm phòng.");
    public static readonly Error RoomGroupNameTaken = Error.Conflict("ROOM_GROUP_NAME_TAKEN", "Tên nhóm phòng đã tồn tại trong khu.");
    public static readonly Error RoomNotInProperty = Error.BusinessRule("ROOM_NOT_IN_PROPERTY", "Có phòng không thuộc khu trọ này.");
}
