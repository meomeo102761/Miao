using Miao.Core.Data;

namespace Miao.Core.Services
{
    public static class CharacterImageStorage
    {
        private static string GroupFolder(Guid groupId)
        {
            var dir = Path.Combine(AppPaths.CharacterImagesRoot, groupId.ToString());
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string SaveGroupCover(Guid groupId, string sourceFilePath)
        {
            DeleteExisting(GroupFolder(groupId), "cover");
            var ext = Path.GetExtension(sourceFilePath);
            var dest = Path.Combine(GroupFolder(groupId), "cover" + ext);
            File.Copy(sourceFilePath, dest, overwrite: true);
            return dest;
        }

        public static string SaveCharacterImage(Guid groupId, Guid characterId, string sourceFilePath)
        {
            var folder = GroupFolder(groupId);
            DeleteExisting(folder, characterId.ToString());
            var ext = Path.GetExtension(sourceFilePath);
            var dest = Path.Combine(folder, characterId + ext);
            File.Copy(sourceFilePath, dest, overwrite: true);
            return dest;
        }

        public static void DeleteGroupFolder(Guid groupId)
        {
            var dir = Path.Combine(AppPaths.CharacterImagesRoot, groupId.ToString());
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        public static void DeleteCharacterImage(Guid groupId, Guid characterId)
            => DeleteExisting(GroupFolder(groupId), characterId.ToString());

        private static void DeleteExisting(string folder, string baseName)
        {
            foreach (var f in Directory.EnumerateFiles(folder, baseName + ".*"))
                File.Delete(f);
        }

        public static string SaveGroupCoverBytes(Guid groupId, byte[] pngBytes)
        {
            DeleteExisting(GroupFolder(groupId), "cover");
            var dest = Path.Combine(GroupFolder(groupId), "cover.png");
            File.WriteAllBytes(dest, pngBytes);
            return dest;
        }

        public static string SaveCharacterImageBytes(Guid groupId, Guid characterId, byte[] pngBytes)
        {
            var folder = GroupFolder(groupId);
            DeleteExisting(folder, characterId.ToString());
            var dest = Path.Combine(folder, characterId + ".png");
            File.WriteAllBytes(dest, pngBytes);
            return dest;
        }

        public static string SaveDescriptionImageBytes(Guid sectionId, Guid blockId, byte[] pngBytes)
        {
            var dir = Path.Combine(AppPaths.CharacterImagesRoot, "descriptions", sectionId.ToString());
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, blockId + ".png");
            File.WriteAllBytes(dest, pngBytes);
            return dest;
        }
    }
}