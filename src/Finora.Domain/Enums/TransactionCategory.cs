namespace Finora.Domain.Enums;

public enum TransactionCategory
{
    // ── Rendimentos (receita) ──
    Salary = 0,
    Investments = 1,
    PurchaseRefunds = 2,
    TaxRefund = 3,
    BenefitsPensions = 4,
    SelfEmployment = 5,
    OtherIncome = 9,

    // ── Alimentação ──
    Groceries = 10,
    Restaurants = 11,
    Cafes = 12,

    // ── Habitação ──
    Rent = 20,
    HouseholdBills = 21,

    // ── Transportes ──
    Fuel = 30,
    PublicTransport = 31,
    Parking = 32,
    CarMaintenance = 33,
    TaxiRideshare = 34,

    // ── Saúde ──
    Pharmacy = 40,
    Health = 41,
    GymSports = 42,

    // ── Lazer ──
    PersonalCare = 50,
    Gifts = 51,
    Leisure = 52,
    Travel = 53,
    Donations = 54,
    Pets = 55,
    Subscriptions = 56,

    // ── Compras ──
    Shopping = 60,
    Clothing = 61,
    HomeFurniture = 62,
    Electronics = 63,
    CreditCard = 64,

    // ── Educação e família ──
    Education = 70,
    Childcare = 71,

    // ── Encargos ──
    Taxes = 80,
    FeesCommissions = 81,
    ProfessionalServices = 82,
    Insurance = 83,

    // ── Outros (despesa) ──
    OtherExpense = 98,

    // ── Transferência ──
    Transfer = 100
}
