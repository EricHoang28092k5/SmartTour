namespace SmartTourCMS.Models
{
    public class UserWithRoleViewModel
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string Email { get; set; }
        public bool IsLockedOut { get; set; } // Trạng thái có đang bị khóa mõm hay không
        public IList<string> Roles { get; set; } // Danh sách chức vụ (Admin, Vendor...)
    }
}