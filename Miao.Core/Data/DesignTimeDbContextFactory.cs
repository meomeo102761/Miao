using Microsoft.EntityFrameworkCore.Design;

namespace Miao.Core.Data
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MiaoDbContext>
    {
        public MiaoDbContext CreateDbContext(string[] args)
        {
            // Đường dẫn tạm — chỉ dùng lúc tạo migration, không ảnh hưởng app thật
            return new MiaoDbContext("design_time_temp.db");
        }
    }
}