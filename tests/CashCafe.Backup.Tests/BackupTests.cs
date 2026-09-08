using CashCafe.Domain;

namespace CashCafe.Backup.Tests;

public class BackupTests
{
    [Fact]
    public void A_backup_is_written_and_can_be_listed()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();

        var result = cafe.Service.Create(cafe.Local);

        result.Succeeded.Should().BeTrue(result.Message);
        result.FileName.Should().EndWith(".cafebak");
        result.Message.Should().Contain("2 student(s)");

        cafe.Local.List().Should().ContainSingle();
    }

    [Fact]
    public void A_backup_contains_a_working_database_and_plain_text_copies()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.Local);

        var stored = cafe.Local.List().Single();
        using var file = cafe.Local.OpenRead(stored.Id);
        using var zip = new System.IO.Compression.ZipArchive(file);

        zip.Entries.Select(e => e.Name).Should().BeEquivalentTo(
            "cashcafe.db", "manifest.json", "balances.csv", "transactions.csv");

        using var reader = new StreamReader(zip.GetEntry("balances.csv")!.Open());
        var balances = reader.ReadToEnd();

        balances.Should().Contain("Carl Jacobs").And.Contain("40,00");
    }

    [Fact]
    public void The_manifest_says_what_is_inside()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.Local, reason: "manual");

        using var file = cafe.Local.OpenRead(cafe.Local.List().Single().Id);
        using var zip = new System.IO.Compression.ZipArchive(file);
        using var stream = zip.GetEntry("manifest.json")!.Open();

        var manifest = System.Text.Json.JsonSerializer.Deserialize<BackupManifest>(stream)!;

        manifest.Students.Should().Be(2);
        manifest.Transactions.Should().Be(3);
        manifest.TotalBalanceOre.Should().Be(cafe.TotalBalances().Ore);
        manifest.DatabaseSha256.Should().HaveLength(64);
        manifest.Reason.Should().Be("manual");
        manifest.Encrypted.Should().BeFalse();
    }

    [Fact]
    public void A_snapshot_is_taken_while_the_cafe_keeps_trading()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();

        // A connection is open and in use, as it would be at a counter mid-service.
        using var busy = cafe.Database.Open();

        cafe.Service.Create(cafe.Local).Succeeded.Should().BeTrue();
        cafe.Service.VerifyLatest(cafe.Local).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Verification_confirms_the_backup_is_actually_restorable()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.Local);

        var check = cafe.Service.VerifyLatest(cafe.Local);

        check.Succeeded.Should().BeTrue(check.Message);
        check.Message.Should().Contain("2 student(s)");
    }

    [Fact]
    public void A_damaged_backup_fails_verification_rather_than_being_trusted()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.Local);

        var path = Path.Combine(cafe.Local.Path, cafe.Local.List().Single().Id);
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        cafe.Service.VerifyLatest(cafe.Local).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Verification_of_an_empty_destination_says_so_plainly()
    {
        using var cafe = new BackupTestCafe();

        var check = cafe.Service.VerifyLatest(cafe.Local);

        check.Succeeded.Should().BeFalse();
        check.Message.Should().Contain("no backups");
    }

    [Fact]
    public void A_destination_is_tested_before_it_is_trusted()
    {
        using var cafe = new BackupTestCafe();

        cafe.Local.Test().Reachable.Should().BeTrue();
        new FolderDestination("/proc/nonsense/nowhere", "Broken share").Test().Reachable.Should().BeFalse();
    }
}

public class RestoreTests
{
    [Fact]
    public void A_restore_brings_back_exactly_what_was_there()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        var before = cafe.TotalBalances();

        cafe.Service.Create(cafe.Local);

        // A disastrous afternoon: someone is charged 40 kr by mistake and a student is added.
        cafe.AddStudent("Mistake", "Student", 999);
        cafe.TotalBalances().Should().NotBe(before);

        var restore = cafe.Service.Restore(cafe.Local, cafe.Local.List().Single().Id);

        restore.Succeeded.Should().BeTrue(restore.Message);
        cafe.TotalBalances().Should().Be(before);
        cafe.Students.All().Should().HaveCount(2);
        cafe.Verifier.Verify().IsClean.Should().BeTrue();
    }

    [Fact]
    public void A_restore_takes_a_safety_copy_of_what_it_is_about_to_replace()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.Local);

        cafe.AddStudent("Later", "Student", 75);
        var beforeRestore = cafe.TotalBalances();

        var safety = new FolderDestination(Path.Combine(cafe.Root, "safety"), "Safety");
        cafe.Service.Restore(cafe.Local, cafe.Local.List().Single().Id, safetyCopyTo: safety);

        safety.List().Should().ContainSingle("the state being replaced must be recoverable");

        // And the safety copy really does hold the state that was replaced.
        cafe.Service.Restore(safety, safety.List().Single().Id).Succeeded.Should().BeTrue();
        cafe.TotalBalances().Should().Be(beforeRestore);
    }

    [Fact]
    public void A_corrupt_backup_is_refused_and_the_live_database_is_left_alone()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        var before = cafe.TotalBalances();
        cafe.Service.Create(cafe.Local);

        var path = Path.Combine(cafe.Local.Path, cafe.Local.List().Single().Id);
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        var restore = cafe.Service.Restore(cafe.Local, cafe.Local.List().Single().Id);

        restore.Succeeded.Should().BeFalse();
        restore.Message.Should().ContainEquivalentOf("nothing was changed");
        cafe.TotalBalances().Should().Be(before, "today's takings must survive a bad backup file");
    }
}

public class EncryptionTests
{
    [Fact]
    public void A_destination_that_leaves_the_computer_will_not_take_an_unencrypted_backup()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();

        var result = cafe.Service.Create(cafe.OffSite);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("passphrase");
        cafe.OffSite.List().Should().BeEmpty();
    }

    [Fact]
    public void An_encrypted_backup_is_unreadable_without_the_passphrase()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.OffSite, BackupTestCafe.Passphrase);

        var bytes = File.ReadAllBytes(Path.Combine(cafe.OffSite.Path, cafe.OffSite.List().Single().Id));
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        text.Should().NotContain("Carl Jacobs", "this is what the cloud provider stores");
        text.Should().NotContain("PK", "not even the zip structure should be visible");
    }

    [Fact]
    public void The_right_passphrase_opens_it_and_a_wrong_one_does_not()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.OffSite, BackupTestCafe.Passphrase);

        cafe.Service.VerifyLatest(cafe.OffSite, BackupTestCafe.Passphrase).Succeeded.Should().BeTrue();

        var wrong = cafe.Service.VerifyLatest(cafe.OffSite, "kaffe-och-bulle-2027");
        wrong.Succeeded.Should().BeFalse();
        wrong.Message.Should().Contain("passphrase is wrong or the file is damaged");
    }

    [Fact]
    public void An_encrypted_backup_can_be_restored()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        var before = cafe.TotalBalances();
        cafe.Service.Create(cafe.OffSite, BackupTestCafe.Passphrase);

        cafe.AddStudent("Later", "Student", 500);

        var restore = cafe.Service.Restore(cafe.OffSite, cafe.OffSite.List().Single().Id, BackupTestCafe.Passphrase);

        restore.Succeeded.Should().BeTrue(restore.Message);
        cafe.TotalBalances().Should().Be(before);
    }

    [Fact]
    public void Tampering_with_an_encrypted_backup_is_detected_not_just_prevented()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();
        cafe.Service.Create(cafe.OffSite, BackupTestCafe.Passphrase);

        var path = Path.Combine(cafe.OffSite.Path, cafe.OffSite.List().Single().Id);
        var bytes = File.ReadAllBytes(path);
        bytes[^20] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        // AES-GCM is authenticated, so an altered file fails to open rather than decrypting
        // to plausible-looking rubbish.
        cafe.Service.VerifyLatest(cafe.OffSite, BackupTestCafe.Passphrase).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_short_passphrase_is_refused()
    {
        var act = () => BackupCrypto.Encrypt(new MemoryStream("x"u8.ToArray()), new MemoryStream(), "kort");

        act.Should().Throw<ArgumentException>().WithMessage("*at least 12*");
    }

    [Fact]
    public void Every_backup_uses_a_fresh_salt_and_nonce()
    {
        var plain = "the same content every time"u8.ToArray();

        var first = new MemoryStream();
        var second = new MemoryStream();
        BackupCrypto.Encrypt(new MemoryStream(plain), first, BackupTestCafe.Passphrase);
        BackupCrypto.Encrypt(new MemoryStream(plain), second, BackupTestCafe.Passphrase);

        first.ToArray().Should().NotEqual(second.ToArray(),
            "identical backups must not produce identical files");
    }

    [Fact]
    public void Something_that_is_not_a_backup_is_rejected_clearly()
    {
        var act = () => BackupCrypto.Decrypt(
            new MemoryStream("just some text"u8.ToArray()), new MemoryStream(), BackupTestCafe.Passphrase);

        act.Should().Throw<InvalidDataException>().WithMessage("*not a Cash Café encrypted backup*");
    }
}

public class RetentionTests
{
    [Fact]
    public void Recent_daily_backups_are_all_kept()
    {
        using var cafe = new BackupTestCafe();
        cafe.Trade();

        var today = DateTimeOffset.UtcNow;
        for (var day = 0; day < 5; day++)
            cafe.Service.Create(cafe.Local, now: today.AddDays(-day));

        cafe.Service.ApplyRetention(cafe.Local, dailyToKeep: 14, now: today).Should().Be(0);
        cafe.Local.List().Should().HaveCount(5);
    }

    [Fact]
    public void The_database_is_flagged_when_it_sits_in_a_synced_folder()
    {
        using var cafe = new BackupTestCafe();
        cafe.Service.DatabaseIsInASyncedFolder().Should().BeFalse();

        var synced = new CashCafe.Data.CafeDatabase(
            Path.Combine(Path.GetTempPath(), "OneDrive", "cafe", "cashcafe.db"));

        new BackupService(synced, cafe.Settings, cafe.Students, cafe.Ledger)
            .DatabaseIsInASyncedFolder().Should()
            .BeTrue("a sync client copying a live database is the most common way people corrupt one");
    }
}
