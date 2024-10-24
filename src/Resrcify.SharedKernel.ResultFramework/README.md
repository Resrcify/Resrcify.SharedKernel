# Resrcify.SharedKernel.ResultFramework

## Description
This repository, **Resrcify.SharedKernel.ResultFramework**, provides a flexible and consistent way to represent operation outcomes in your applications using the Result pattern. The goal of this library is to simplify handling success and error flows by providing unified types and methods for both success and failure cases using railway-oriented programming, reducing code duplication and making error handling more maintainable.

## Prerequisites
Before using **Resrcify.SharedKernel.ResultFramework**, ensure that your project meets the following requirements:

- .NET 8.0 is installed.

## Installation
To integrate **Resrcify.SharedKernel.ResultFramework** into your project, you can either clone the source code or install the NuGet package, depending on your preference.

### Download and reference the project files
1. Clone this repository
```bash
git clone https://github.com/Resrcify/Resrcify.SharedKernel.git
```
2. Add the **Resrcify.SharedKernel.ResultFramework** project to your solution/project.

- By referencing the project in your ``.csproj`` file
    ```xml
    <ProjectReference Include="../path/to/Resrcify.SharedKernel.ResultFramework.csproj" />
    ```
- Or by using the command line to reference the project
    ```bash
    dotnet add reference path/to/Resrcify.SharedKernel.ResultFramework.csproj
    ```

### Download and reference Nuget package
1. Add the package from NuGet:
- By referencing in your ``.csproj`` file
    ```xml
    <PackageReference Include="Resrcify.SharedKernel.ResultFramework" Version="1.8.5" />
    ```
- Or by using the command line
    ```bash
    dotnet add package Resrcify.SharedKernel.ResultFramework
    ```

## Configuration
No specific configuration is required; the classes can be integrated directly into projects as they are.

## Usage
### Returning a Simple Result in a Method
The Result pattern allows methods to return success or failure without throwing exceptions. Each Result contains either a value in the success case or an error in the failure case.
```csharp
using Resrcify.SharedKernel.ResultFramework.Primitives;

public Result<User> GetUserById(string userId)
{
    if (string.IsNullOrWhiteSpace(userId))
        return Error.Validation("Error.InvalidUserId", "User Id cannot be null or empty.");

    var user = _userRepository.FindById(userId);

    if (user is null)
        return Error.NotFound("Error.UserNotFound", $"User with Id '{userId}' was not found.");

    return Result.Success(user);
}
```
In this example
- If the userId is invalid, the method returns a failure Result using a predefined Validation error.
- If the user cannot be found, a NotFound error is returned.
- If the user is found, a successful Result<User> is returned with the user object.
- Please note that you can also rely on the implicit operator and return the user without wrapping it into a result object.

### Handling Errors and Successful Results
You can use the IsSuccess/IsFailure properties or the Errors property to handle the result.
```csharp
using Resrcify.SharedKernel.ResultFramework.Primitives;

public Result<User> ProcessUser()
{
    Result<User> userResult = GetUserResult();
    if(userResult.IsSuccess)
        return ProcessUser(userResult.Value)

    foreach(var error in userResult.Errors)
        Console.WriteLine(error.Message);

    return userResult;
}
```
In this example
- If the userResult is a success it proceeds with processing the user and continues the work in the success track of the railway. Please note how the actual User is sent in to the method using the Value property.
- If the userResult is not a success (the property IsFailure will also be marked true), logging is performed over the errors.
- Finally the railway has changed track from a success to a failure.
### Combining Multiple Results
```csharp
using Resrcify.SharedKernel.ResultFramework.Primitives;

public Result<UserProfile> GetUserProfile(string userId)
{
    Result<User> userResult = _userService.GetUserResultById(userId);
    Result<List<Order>> ordersResult = _orderService.GetOrdersResultForUser(userId);

    var combinedResult = Result.Combine(userResult, ordersResult);

    if (combinedResult.IsFailure)
        return Result.Failure<UserProfile>(combinedResult.Errors);

    var userProfile = new UserProfile
    {
        User = userResult.Value,
        Orders = ordersResult.Value
    };
    PerformSideEffect(userProfile)
    return Result.Success<UserProfile>(userProfile);
}
```
In this example
- The Result.Combine method is used to combine two results (userResult and ordersResult). If either fails, it returns the error (this needs to be wrapped in a new Result of type UserProfile, since the signature is different for the combined result).
- If both succeed, a new UserProfile object is created and returned as a successful result.

### Validation
```csharp
using Resrcify.SharedKernel.ResultFramework.Primitives;

public Result<UserProfile> GetUserProfile(string userId)
{
    User user = _userService.GetUserById(userId);
    List<Order> orders = _orderService.GetOrdersForUser(userId);

    var ordersResult = Result
        .Create(orders)
        .Ensure(
            orders => orders.Count > 0,
            new Error(
                ErrorTypes.Validation,
                "Empty",
                "Orders cannot be empty"))

    if(ordersResult.IsFailure)
        return Result.Failure<UserProfile>(ordersResult.Errors);

    var userProfile = new UserProfile
    {
        User = user,
        Orders = ordersResult.Value
    };

    return Result.Success<UserProfile>(userProfile);
}
```
In this example
- No values are retrieved wrapped in Result objects.
- Orders are then wrapped into a result object and validation is performed using the Ensure methods.
- If the validation failed the errors are wrapped into a new Result object due to the difference in signature and returned.
### Functional Programming by Chaining Extension Methods
**Create** in functional programming, Create is used to instantiate a Result object.

**Map** is a function that transforms the value inside a Result. When the Result is Success, the function provided is applied to the inner value, resulting in a new Result that reflects the transformation. Otherwise the result is treated as a failure.

**Bind** is used for chaining operations where each step may result in a new Result. This method allows for transformations that return another Result, successfully changing the return type to the new result so it can be used in further operations.

**Tap** allows you to execute side effects, such as logging or notifications, without altering the value within the Result.

**Ensure** acts as a validation step within the pipeline. It enforces conditions on the Result, and if these conditions are not met, it returns an error.

**Match** provides a way to handle both success and failure cases in a unified manner. It allows you to define actions based on the outcome of the Result, facilitating branching logic in a functional style. This method is often used at the end of a pipeline to determine the final action based on whether the result was successful or resulted in an error.
```csharp
public Result<UserProfile> GetUserProfile(string userId)
    => Result
        .Combine(
            await _userService.GetUserResultByIdAsync(userId),
            await _orderService.GetOrdersResultForUserAsync(userId))
        .Map(tuple => new UserProfile
        {
            User = tuple.item1,
            Orders = tuple.item2
        })
        .Tap(userProfile => PerformSideEffect(userProfile));
```
In this example
- With the help of extensive synch and async extensions methods method chaining is performed to create a single expression which achives the same result as the Combining Multiple Results example.

## Sample projects
Extensive use of the result pattern, with or without chaining of extension methods has been showcased in the sample project [**Resrcify.SharedKernel.WebApiExample**](../../samples/Resrcify.SharedKernel.WebApiExample).

## Suggestions for further development

Here are a few ideas for extending this library in the future:

- **Custom Error Handling Strategies:** Allow the definition and usage of custom error types beyond the predefined ones (e.g., NotFound, Validation, Conflict).