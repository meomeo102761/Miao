using Microsoft.EntityFrameworkCore;
using Miao.Core.Models;

namespace Miao.Core.Data
{
    public class MiaoDbContext : DbContext
    {
        private readonly string _dbPath;

        public DbSet<Novel> Novels => Set<Novel>();
        public DbSet<Chapter> Chapters => Set<Chapter>();
        public DbSet<NoteEntry> Notes => Set<NoteEntry>();
        public DbSet<NovelSource> NovelSources => Set<NovelSource>();
        public DbSet<GlossarySet> GlossarySets => Set<GlossarySet>();
        public DbSet<GlossarySetEntry> GlossarySetEntries => Set<GlossarySetEntry>();
        public DbSet<NovelGlossarySet> NovelGlossarySets => Set<NovelGlossarySet>();
        public DbSet<GlossaryGroup> GlossaryGroups => Set<GlossaryGroup>();
        public DbSet<CustomLibrary> CustomLibraries => Set<CustomLibrary>();
        public DbSet<CustomLibraryNovel> CustomLibraryNovels => Set<CustomLibraryNovel>();
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<NovelTag> NovelTags => Set<NovelTag>();
        public DbSet<NovelLink> NovelLinks => Set<NovelLink>();
        public DbSet<Volume> Volumes => Set<Volume>();

        public DbSet<CharacterGroup> CharacterGroups => Set<CharacterGroup>();
        public DbSet<Character> Characters => Set<Character>();
        public DbSet<CharacterAlias> CharacterAliases => Set<CharacterAlias>();
        public DbSet<NovelCharacterGroup> NovelCharacterGroups => Set<NovelCharacterGroup>();
        public DbSet<CharacterInfoSection> CharacterInfoSections => Set<CharacterInfoSection>();
        public DbSet<CharacterInfoEntry> CharacterInfoEntries => Set<CharacterInfoEntry>();
        public DbSet<CharacterDescriptionSection> CharacterDescriptionSections => Set<CharacterDescriptionSection>();
        public DbSet<CharacterDescriptionBlock> CharacterDescriptionBlocks => Set<CharacterDescriptionBlock>();
        public DbSet<CharacterFaction> CharacterFactions => Set<CharacterFaction>();

        public DbSet<PendingSync> PendingSyncs => Set<PendingSync>();

        public MiaoDbContext(string dbPath)
        {
            _dbPath = dbPath;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite($"Data Source={_dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Novel>()
                .HasMany(n => n.Chapters)
                .WithOne(c => c.Novel)
                .HasForeignKey(c => c.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GlossarySet>()
                .HasMany(s => s.Entries)
                .WithOne(e => e.GlossarySet)
                .HasForeignKey(e => e.GlossarySetId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GlossarySet>()
                .HasOne(s => s.OwnerNovel)
                .WithMany()
                .HasForeignKey(s => s.OwnerNovelId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<NovelGlossarySet>()
                .HasOne(ns => ns.Novel)
                .WithMany()
                .HasForeignKey(ns => ns.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NovelGlossarySet>()
                .HasOne(ns => ns.GlossarySet)
                .WithMany()
                .HasForeignKey(ns => ns.GlossarySetId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<NovelGlossarySet>()
                .HasIndex(ns => new { ns.NovelId, ns.GlossarySetId })
                .IsUnique(); 

            modelBuilder.Entity<Chapter>()
                .HasMany(c => c.Notes)
                .WithOne(note => note.Chapter)
                .HasForeignKey(note => note.ChapterId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Novel>()
                .HasMany<NovelSource>()
                .WithOne(s => s.Novel)
                .HasForeignKey(s => s.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CustomLibrary>()
                .HasMany(l => l.Items)
                .WithOne(i => i.CustomLibrary)
                .HasForeignKey(i => i.CustomLibraryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CustomLibraryNovel>()
                .HasOne(i => i.Novel)
                .WithMany()
                .HasForeignKey(i => i.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NovelTag>()
                .HasOne(nt => nt.Novel)
                .WithMany()
                .HasForeignKey(nt => nt.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NovelTag>()
                .HasOne(nt => nt.Tag)
                .WithMany()
                .HasForeignKey(nt => nt.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NovelLink>()
                .HasOne(l => l.Novel)
                .WithMany()
                .HasForeignKey(l => l.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CharacterGroup>()
                .HasMany(g => g.Characters)
                .WithOne(c => c.CharacterGroup)
                .HasForeignKey(c => c.CharacterGroupId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CharacterGroup>()
                .HasOne(g => g.OwnerNovel)
                .WithMany()
                .HasForeignKey(g => g.OwnerNovelId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Character>()
                .HasMany(c => c.Aliases)
                .WithOne(a => a.Character)
                .HasForeignKey(a => a.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NovelCharacterGroup>()
                .HasOne(nc => nc.Novel)
                .WithMany()
                .HasForeignKey(nc => nc.NovelId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<NovelCharacterGroup>()
                .HasOne(nc => nc.CharacterGroup)
                .WithMany()
                .HasForeignKey(nc => nc.CharacterGroupId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<NovelCharacterGroup>()
                .HasIndex(nc => new { nc.NovelId, nc.CharacterGroupId })
                .IsUnique(); 
                
            modelBuilder.Entity<CharacterInfoSection>()
                .HasOne(s => s.Character)
                .WithMany()
                .HasForeignKey(s => s.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CharacterInfoEntry>()
                .HasOne(e => e.Section)
                .WithMany(s => s.Entries)
                .HasForeignKey(e => e.CharacterInfoSectionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CharacterDescriptionSection>()
                .HasOne(s => s.Character)
                .WithMany()
                .HasForeignKey(s => s.CharacterId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CharacterDescriptionBlock>()
                .HasOne(b => b.Section)
                .WithMany(s => s.Blocks)
                .HasForeignKey(b => b.CharacterDescriptionSectionId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<CharacterFaction>()
                .HasOne(f => f.CharacterGroup)
                .WithMany()
                .HasForeignKey(f => f.CharacterGroupId)
                .OnDelete(DeleteBehavior.Cascade); 

            modelBuilder.Entity<Character>()
                .HasOne(c => c.Faction)
                .WithMany()
                .HasForeignKey(c => c.FactionId)
                .OnDelete(DeleteBehavior.SetNull); 
        }
    }
}