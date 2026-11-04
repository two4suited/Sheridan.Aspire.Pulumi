using Sheridan.Aspire.Pulumi.Azure.ApiManagement;
using Xunit;

namespace Sheridan.Aspire.Pulumi.Tests;

public sealed class ApiManagementTests
{
    [Fact]
    public void Sku_defaults_to_Developer_with_one_unit()
    {
        var o = new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p" };
        Assert.Equal(AzureApiManagementSku.Developer, o.Sku);
        Assert.Equal(1, o.Capacity);
    }

    [Fact]
    public void Consumption_defaults_to_zero_capacity()
    {
        var o = new AzureApiManagementOptions { PublisherEmail = "a@b.c", PublisherName = "p", Sku = AzureApiManagementSku.Consumption };
        Assert.Equal(0, o.Capacity);
    }

    [Fact]
    public void BuildPolicy_prefers_a_complete_document_then_wraps_inbound_statements()
    {
        Assert.Equal("<policies/>", AzureApiManagementResource.BuildPolicy("<policies/>", ["<x/>"]));
        Assert.Null(AzureApiManagementResource.BuildPolicy(null, []));
        var wrapped = AzureApiManagementResource.BuildPolicy(null, ["<rate-limit calls=\"5\" renewal-period=\"1\" />"]);
        Assert.Contains("<inbound><base /><rate-limit", wrapped);
        Assert.Contains("<on-error><base /></on-error>", wrapped);
    }

    [Fact]
    public void ValidateFragment_rejects_malformed_xml_and_blank_input()
    {
        AzureApiManagementResource.ValidateFragment("<a/><b/>", "p");
        Assert.Throws<ArgumentException>(() => AzureApiManagementResource.ValidateFragment("<a>", "p"));
        Assert.Throws<ArgumentException>(() => AzureApiManagementResource.ValidateFragment(" ", "p"));
    }
}
