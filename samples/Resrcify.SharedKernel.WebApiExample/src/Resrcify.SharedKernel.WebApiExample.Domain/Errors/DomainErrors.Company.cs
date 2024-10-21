using System;
using Resrcify.SharedKernel.ResultFramework.Primitives;

namespace Resrcify.SharedKernel.WebApiExample.Domain.Errors;

public static class DomainErrors
{
    public static class Company
    {
        public static readonly Func<string, Error> OrganizationNumberAlreadyExist = (organizationNumber) => Error.Conflict(
            "Company.OrganizationNumberAlreadyExist",
            $"Company with the organization number {organizationNumber} already exist.");
    }
    public static class CompanyId
    {
        public static readonly Error Empty = Error.Validation(
            "CompanyId.Empty",
            $"Cannot be empty.");
    }
    public static class ContactId
    {
        public static readonly Error Empty = Error.Validation(
            "ContactId.Empty",
            $"Cannot be empty.");
    }
    public static class Contact
    {
        public static readonly Func<string, Error> EmailAlreadyExist = (email) => Error.Conflict(
            "Contact.EmailAlreadyExist",
            $"Contact with the email {email} already exist.");
        public static readonly Func<string, Error> NotFound = (email) => Error.NotFound(
            "Contact.NotFound",
            $"Contact with the email {email} was not found.");
    }
    public static class Name
    {
        public static readonly Func<string, int, Error> TooLong = (value, maxLength) => Error.Validation(
            "Name.TooLong",
            $"{value} exceeds the max length of {maxLength}.");
        public static readonly Func<string, int, Error> TooShort = (value, minLength) => Error.Validation(
            "Name.TooShort",
            $"{value} is less than the min length of {minLength}.");
        public static readonly Error Empty = Error.Validation(
            "Name.Empty",
            $"Cannot be empty.");
        public static readonly Error Invalid = Error.Validation(
            "Name.Invalid",
            $"Name contains invalid characters.");
        public static readonly Func<string, Error> Identical = (value) => Error.Conflict(
            "Name.Identical",
            $"Name {value} is identical to current name.");
    }
    public static class OrganizationNumber
    {
        public static readonly Error Empty = Error.Validation(
            "OrganizationNumber.Empty",
            $"Cannot be empty.");
        public static readonly Error InvalidLength = Error.Validation(
            "OrganizationNumber.InvalidLength",
            $"Number has to be exactly 10 digits.");
        public static readonly Error InvalidStartingDigit = Error.Validation(
            "OrganizationNumber.InvalidStartingDigit",
            $"Number must start with 1, 2, 3, 5, 7, 8, or 9.");
        public static readonly Error InvalidChecksum = Error.Validation(
            "OrganizationNumber.InvalidChecksum",
            $"Checksum (last digit) is not valid according to Luhn algorithm.");
    }
    public static class Email
    {
        public static readonly Func<string, int, Error> TooLong = (value, maxLength) => Error.Validation(
            "Email.TooLong",
            $"{value} exceeds the max length of {maxLength}.");
        public static readonly Func<string, int, Error> TooShort = (value, minLength) => Error.Validation(
            "Email.TooShort",
            $"{value} is less than the min length of {minLength}.");
        public static readonly Error Empty = Error.Validation(
            "Email.Empty",
            $"Cannot be empty.");
        public static readonly Error Invalid = Error.Validation(
            "Email.Invalid",
            $"Email is invalid.");
    }
}