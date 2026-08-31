using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Miao.Core.Data;
using Miao.Core.Models;

namespace Miao.Core.Services
{
    public static class GlossaryGroupService
    {
        public static List<GlossaryGroup> GetGroups(MiaoDbContext db, bool isShared) =>
            db.GlossaryGroups.Where(g => g.IsShared == isShared)
                .OrderBy(g => g.SortOrder).ThenBy(g => g.Name).ToList();

        public static GlossaryGroup CreateGroup(MiaoDbContext db, string name, bool isShared)
        {
            var maxOrder = db.GlossaryGroups.Where(g => g.IsShared == isShared).Select(g => (int?)g.SortOrder).Max() ?? -1;
            var group = new GlossaryGroup { Name = name, IsShared = isShared, SortOrder = maxOrder + 1 };
            db.GlossaryGroups.Add(group);
            db.SaveChanges();
            return group;
        }

        public static void AddSetToGroup(MiaoDbContext db, Guid groupId, Guid setId)
        {
            var group = db.GlossaryGroups.Find(groupId);
            var set = db.GlossarySets.Find(setId);
            if (group == null || set == null) return;
            if (group.IsShared != set.IsShared)
                throw new InvalidOperationException("Không thể thêm bộ tên khác loại (chung/riêng) vào nhóm này.");

            db.Entry(group).Collection(g => g.Sets).Load();
            if (!group.Sets.Any(s => s.Id == setId))
            {
                group.Sets.Add(set);
                db.SaveChanges();
            }
        }

        public static void RemoveSetFromGroup(MiaoDbContext db, Guid groupId, Guid setId)
        {
            var group = db.GlossaryGroups.Find(groupId);
            if (group == null) return;

            db.Entry(group).Collection(g => g.Sets).Load();
            var set = group.Sets.FirstOrDefault(s => s.Id == setId);
            if (set != null) { group.Sets.Remove(set); db.SaveChanges(); }
        }
    }
}