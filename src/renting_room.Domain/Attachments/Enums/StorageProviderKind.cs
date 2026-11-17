namespace renting_room.Domain.Attachments;

/// <summary>Nơi file vật lý nằm — lưu theo từng tệp để đổi cấu hình Local ⇄ S3 không làm mất file cũ (FS-BR-15).</summary>
public enum StorageProviderKind
{
    Local,
    S3
}
