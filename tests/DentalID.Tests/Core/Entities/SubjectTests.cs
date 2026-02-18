using Xunit;
using DentalID.Core.Entities;
using System;
using System.Collections.Generic;

namespace DentalID.Tests.Core.Entities;

public class SubjectTests
{
    [Fact]
    public void LatestDentalCode_ShouldReturnNA_WhenNoImages()
    {
        var subject = new Subject();
        Assert.Equal("N/A", subject.LatestDentalCode);
    }

    [Fact]
    public void LatestDentalCode_ShouldReturnLatestCode_WhenMultipleImagesExist()
    {
        var subject = new Subject();
        subject.DentalImages.Add(new DentalImage { UploadedAt = DateTime.UtcNow.AddDays(-2), FingerprintCode = "OLD" });
        subject.DentalImages.Add(new DentalImage { UploadedAt = DateTime.UtcNow, FingerprintCode = "NEW" });
        subject.DentalImages.Add(new DentalImage { UploadedAt = DateTime.UtcNow.AddDays(-1), FingerprintCode = "MID" });

        Assert.Equal("NEW", subject.LatestDentalCode);
    }

    [Fact]
    public void LatestDentalCode_ShouldHandleNullCode()
    {
        var subject = new Subject();
        subject.DentalImages.Add(new DentalImage { UploadedAt = DateTime.UtcNow, FingerprintCode = null });

        Assert.Equal("N/A", subject.LatestDentalCode);
    }
}
