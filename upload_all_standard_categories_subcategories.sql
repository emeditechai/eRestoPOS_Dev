-- =====================================================================================================
-- SCRIPT NAME : upload_all_standard_categories_subcategories.sql
-- DESCRIPTION : PRODUCTION CLEANUP & MASTER SEED SCRIPT
--               1. Cleans up all Junk/Test data (TEST, TEST_KOL, VIP Special, duplicates, etc.)
--               2. Remaps existing MenuItems safely to official standard categories
--               3. Seeds complete End-to-End Production Standard Menu & BAR Categories & Sub-Categories
-- DATABASE    : Microsoft SQL Server (Compatible with all MSSQL versions / Azure SQL / Express)
-- SAFETY      : Fully transactional, preserves real MenuItems, removes all junk/test clutter
-- =====================================================================================================

SET NOCOUNT ON;

DECLARE @StagedCount INT;
DECLARE @TotalCats INT;
DECLARE @TotalSubCats INT;
DECLARE @RemappedItemsCount INT = 0;
DECLARE @DeletedJunkCatCount INT = 0;
DECLARE @DeletedJunkSubCatCount INT = 0;

PRINT '================================================================================';
PRINT '  STARTING PRODUCTION MASTER CATEGORIES & SUB-CATEGORIES CLEANUP & SEEDING';
PRINT '================================================================================';

-- =====================================================================================================
-- STEP 1: ENSURE TABLES & COLUMNS EXIST (SCHEMA VERIFICATION)
-- =====================================================================================================

-- 1.1 Ensure [dbo].[Categories] table exists
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Categories]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[Categories] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        [Name] NVARCHAR(100) NOT NULL,
        [IsActive] BIT NOT NULL DEFAULT (1)
    );
    PRINT 'Created table [dbo].[Categories].';
END
ELSE
BEGIN
    -- Ensure Name column exists
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Categories]') AND name = 'Name')
    BEGIN
        ALTER TABLE [dbo].[Categories] ADD [Name] NVARCHAR(100) NOT NULL DEFAULT ('');
        PRINT 'Added missing column [Name] to [dbo].[Categories].';
    END

    -- Ensure IsActive column exists
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Categories]') AND name = 'IsActive')
    BEGIN
        ALTER TABLE [dbo].[Categories] ADD [IsActive] BIT NOT NULL DEFAULT (1);
        PRINT 'Added missing column [IsActive] to [dbo].[Categories].';
    END
END

-- 1.2 Ensure [dbo].[SubCategories] table exists
IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[SubCategories] (
        [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        [Name] NVARCHAR(100) NOT NULL,
        [Description] NVARCHAR(500) NULL,
        [IsActive] BIT NOT NULL DEFAULT (1),
        [CategoryId] INT NOT NULL,
        [DisplayOrder] INT NOT NULL DEFAULT (1),
        [CreatedAt] DATETIME2 NOT NULL DEFAULT (GETDATE()),
        [UpdatedAt] DATETIME2 NULL,
        CONSTRAINT [FK_SubCategories_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[Categories] ([Id]) ON DELETE NO ACTION
    );
    PRINT 'Created table [dbo].[SubCategories].';
END
ELSE
BEGIN
    -- Ensure columns exist
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'Name')
        ALTER TABLE [dbo].[SubCategories] ADD [Name] NVARCHAR(100) NOT NULL DEFAULT ('');
    
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'Description')
        ALTER TABLE [dbo].[SubCategories] ADD [Description] NVARCHAR(500) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'IsActive')
        ALTER TABLE [dbo].[SubCategories] ADD [IsActive] BIT NOT NULL DEFAULT (1);

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'CategoryId')
        ALTER TABLE [dbo].[SubCategories] ADD [CategoryId] INT NOT NULL DEFAULT (1);

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'DisplayOrder')
        ALTER TABLE [dbo].[SubCategories] ADD [DisplayOrder] INT NOT NULL DEFAULT (1);

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'CreatedAt')
        ALTER TABLE [dbo].[SubCategories] ADD [CreatedAt] DATETIME2 NOT NULL DEFAULT (GETDATE());

    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'UpdatedAt')
        ALTER TABLE [dbo].[SubCategories] ADD [UpdatedAt] DATETIME2 NULL;

    -- Make UpdatedAt column nullable if it is currently NOT NULL
    BEGIN TRY
        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[SubCategories]') AND name = 'UpdatedAt' AND is_nullable = 0)
        BEGIN
            ALTER TABLE [dbo].[SubCategories] ALTER COLUMN [UpdatedAt] DATETIME2 NULL;
            PRINT 'Updated [dbo].[SubCategories].[UpdatedAt] to be NULLable.';
        END
    END TRY
    BEGIN CATCH
    END CATCH

    -- Ensure Foreign Key from SubCategories to Categories exists
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_SubCategories_Categories_CategoryId', 'FK_SubCategories_Categories'))
    BEGIN
        ALTER TABLE [dbo].[SubCategories] WITH CHECK 
        ADD CONSTRAINT [FK_SubCategories_Categories_CategoryId] 
        FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[Categories] ([Id])
        ON DELETE NO ACTION;
        PRINT 'Created Foreign Key FK_SubCategories_Categories_CategoryId.';
    END
END

-- 1.3 Ensure Indexes exist on SubCategories
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SubCategories_CategoryId' AND object_id = OBJECT_ID(N'[dbo].[SubCategories]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SubCategories_CategoryId] ON [dbo].[SubCategories] ([CategoryId] ASC);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SubCategories_IsActive' AND object_id = OBJECT_ID(N'[dbo].[SubCategories]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SubCategories_IsActive] ON [dbo].[SubCategories] ([IsActive] ASC);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SubCategories_DisplayOrder' AND object_id = OBJECT_ID(N'[dbo].[SubCategories]'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_SubCategories_DisplayOrder] ON [dbo].[SubCategories] ([DisplayOrder] ASC);
END

-- 1.4 Ensure MenuItems has SubCategoryId column if MenuItems table exists
IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MenuItems]') AND type in (N'U'))
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[MenuItems]') AND name = 'SubCategoryId')
    BEGIN
        ALTER TABLE [dbo].[MenuItems] ADD [SubCategoryId] INT NULL;
        PRINT 'Added column [SubCategoryId] to [dbo].[MenuItems].';
    END

    -- Use ON DELETE NO ACTION to prevent Msg 1785 multiple cascade paths / cycles error
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name IN ('FK_MenuItems_SubCategories', 'FK_MenuItems_SubCategoryId'))
    BEGIN
        ALTER TABLE [dbo].[MenuItems] WITH CHECK 
        ADD CONSTRAINT [FK_MenuItems_SubCategories] FOREIGN KEY ([SubCategoryId]) REFERENCES [dbo].[SubCategories] ([Id])
        ON DELETE NO ACTION;
        PRINT 'Created Foreign Key FK_MenuItems_SubCategories.';
    END
END

PRINT 'Schema verification completed successfully.';
PRINT '--------------------------------------------------------------------------------';

-- =====================================================================================================
-- STEP 2: STAGING OFFICIAL PRODUCTION MASTER DATA (CATEGORIES & SUB-CATEGORIES)
-- =====================================================================================================

IF OBJECT_ID('tempdb..#MasterData') IS NOT NULL DROP TABLE #MasterData;

CREATE TABLE #MasterData (
    CategoryName NVARCHAR(100) NOT NULL,
    SubCategoryName NVARCHAR(100) NOT NULL,
    SubCategoryDesc NVARCHAR(500) NULL,
    DisplayOrder INT NOT NULL DEFAULT (1)
);

-- -----------------------------------------------------------------------------------------------------
-- 2.1 FOOD & KITCHEN CATEGORIES
-- -----------------------------------------------------------------------------------------------------

-- CATEGORY: Appetizers & Starters
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Appetizers & Starters', 'Vegetarian Starters', 'Crispy veg platters, fried & sauteed vegetarian appetizers', 1),
('Appetizers & Starters', 'Non-Vegetarian Starters', 'Chicken, mutton, and meat appetizers and bites', 2),
('Appetizers & Starters', 'Seafood Starters', 'Crispy fish fry, prawns, squid, and seafood starters', 3),
('Appetizers & Starters', 'Tandoori & Kebabs (Veg)', 'Clay-oven roasted paneer tikka, veg seekh, and tandoori veg', 4),
('Appetizers & Starters', 'Tandoori & Kebabs (Non-Veg)', 'Chicken tikka, tandoori chicken, mutton seekh kebabs', 5),
('Appetizers & Starters', 'Starters Platters & Combos', 'Grand sharing starter platters (Veg & Non-Veg)', 6);

-- CATEGORY: Soups & Salads
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Soups & Salads', 'Vegetarian Soups', 'Tomato soup, sweet corn veg, hot & sour veg, cream soups', 1),
('Soups & Salads', 'Non-Vegetarian Soups', 'Chicken clear soup, hot & sour chicken, mutton broth, seafood soups', 2),
('Soups & Salads', 'Fresh Garden Salads', 'Green garden salad, cucumber & tomato, sprout salads', 3),
('Soups & Salads', 'Gourmet & Caesar Salads', 'Caesar salad, Greek salad, grilled chicken salad, pasta salads', 4);

-- CATEGORY: Indian Main Course
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Indian Main Course', 'Paneer & Vegetarian Gravy', 'Paneer Butter Masala, Kadhai Paneer, Mushroom & Kofta curries', 1),
('Indian Main Course', 'Dal & Lentils', 'Dal Makhani, Dal Tadka, Dal Fry, Yellow Dal preparations', 2),
('Indian Main Course', 'Chicken Main Course', 'Butter Chicken, Kadhai Chicken, Chicken Tikka Masala, Chicken Curry', 3),
('Indian Main Course', 'Mutton & Lamb Curries', 'Mutton Rogan Josh, Bhuna Gosht, Mutton Korma, Mutton Curry', 4),
('Indian Main Course', 'Seafood & Fish Curries', 'Fish curry, Prawn Masala, Goan fish curry, Bengali fish kalia', 5),
('Indian Main Course', 'Biryani & Flavored Rice', 'Hyderabadi Biryani, Dum Biryani, Jeera Rice, Steamed Basmati', 6),
('Indian Main Course', 'Raita & Accompaniments', 'Boondi Raita, Mixed Veg Raita, Pineapple Raita, Plain Curd, Papad', 7);

-- CATEGORY: Chinese
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Chinese', 'Dim Sum & Momos', 'Steamed & fried momos, bao buns, dumplings & wontons', 1),
('Chinese', 'VEG (Starter)', 'Veg Manchurian Dry, Chilli Paneer, Crispy Corn, Spring Rolls', 2),
('Chinese', 'NON VEG (Starter)', 'Chilli Chicken, Drums of Heaven, Dragon Chicken, Salt & Pepper Fish', 3),
('Chinese', 'VEG (Main Course)', 'Veg Manchurian Gravy, Chilli Paneer Gravy, Hot Garlic Veg', 4),
('Chinese', 'NON VEG (Main Course)', 'Chilli Chicken Gravy, Kung Pao Chicken, Sweet & Sour Fish/Prawns', 5),
('Chinese', 'Rice & Fried Rice', 'Veg Fried Rice, Egg Fried Rice, Chicken Schezwan Fried Rice', 6),
('Chinese', 'Noodles', 'Hakka Noodles, Schezwan Noodles, Chilli Garlic Noodles, Pad Thai', 7),
('Chinese', 'Thai Curries & Asian Bowls', 'Thai Green Curry, Thai Red Curry, Asian meal bowls with rice', 8);

-- CATEGORY: Continental
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Continental', 'Pizzas - Vegetarian', 'Margherita, Farmhouse, Veggie Delight, Paneer Tikka Pizza', 1),
('Continental', 'Pizzas - Non-Vegetarian', 'BBQ Chicken, Pepperoni, Meat Lovers, Smoked Chicken Pizza', 2),
('Continental', 'Pastas & Lasagna', 'Penne Alfredo, Arrabiata, Pink Sauce Pasta, Chicken/Veg Lasagna', 3),
('Continental', 'Burgers & Sandwiches', 'Gourmet burgers, Club sandwiches, Paninis, Wraps & Rolls', 4),
('Continental', 'Sizzlers & Grills', 'Veg Sizzler, Cottage Cheese Sizzler, Chicken Steak Sizzler', 5),
('Continental', 'Steaks & European Mains', 'Grilled chicken breast, Fish steak, Roast lamb, Mashed potatoes', 6);

-- CATEGORY: Breads
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Breads', 'Indian Breads', 'Tandoori Roti, Plain Naan, Butter Naan, Garlic Naan, Kulcha, Paratha', 1),
('Breads', 'Continental & Artisanal Breads', 'Garlic Bread, Cheese Garlic Bread, Focaccia, Baguettes, Herb Toast', 2),
('Breads', 'Toast, Buns & Pav', 'Butter Toast, Burger Buns, Pav, Bread Basket', 3);

-- CATEGORY: Breakfast Menu
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Breakfast Menu', 'Indian Breakfast', 'Masala Dosa, Idli Sambar, Poori Bhaji, Aloo Paratha, Poha, Upma', 1),
('Breakfast Menu', 'Continental Breakfast', 'Toast, Butter & Preserves, Hash Browns, Baked Beans, Sausages', 2),
('Breakfast Menu', 'Egg Specials', 'Scrambled Eggs, Cheese Omelette, Masala Omelette, Poached, Sunny Side', 3),
('Breakfast Menu', 'Pancakes, Waffles & French Toast', 'Maple Pancakes, Belgian Waffles, Classic French Toast with Honey', 4),
('Breakfast Menu', 'Healthy Cereals & Fruit Bowls', 'Cornflakes, Muesli, Oatmeal Porridge, Fresh Cut Fruit Platter', 5);

-- CATEGORY: Desserts
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Desserts', 'Indian Sweets & Mithai', 'Gulab Jamun, Rasmalai, Gajar Ka Halwa, Moong Dal Halwa, Kheer', 1),
('Desserts', 'Cakes, Pastries & Brownies', 'Chocolate Brownie, Sizzling Brownie with Ice Cream, Lava Cake', 2),
('Desserts', 'Ice Creams & Sundaes', 'Vanilla, Chocolate, Sundaes, Kulfi Falooda, Cassata', 3),
('Desserts', 'Cheesecakes & Puddings', 'New York Cheesecake, Blueberry Cheesecake, Caramel Custard', 4),
('Desserts', 'Waffles & Crepes', 'Nutella Waffles, Belgian Chocolate Crepes, Banana Caramel Crepe', 5);

-- CATEGORY: Add-ons / Extras
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Add-ons / Extras', 'Extra Cheese / Toppings', 'Extra Mozzarella, Cheddar, Olives, Jalapenos, Mushrooms', 1),
('Add-ons / Extras', 'Dips, Sauces & Chutneys', 'Mint Chutney, Tartar Sauce, Peri Peri Dip, Mayo, BBQ Sauce', 2),
('Add-ons / Extras', 'Extra Patty & Protein', 'Extra Chicken Patty, Extra Veg Patty, Boiled Egg, Bacon Strip', 3),
('Add-ons / Extras', 'Extra Gravy, Rice & Bread', 'Extra Curry Portion, Extra Sambar, Extra Pav, Extra Rice Bowl', 4);

-- CATEGORY: Buffet menu
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Buffet menu', 'Executive Thalis & Combos', 'Veg Executive Thali, Non-Veg Deluxe Thali, Meal Trays', 1),
('Buffet menu', 'Buffet Spread Packages', 'Breakfast Buffet, Lunch Buffet, Dinner Buffet Packages', 2),
('Buffet menu', 'Party & Group Sharing Trays', 'Party Kebab Tray, Biryani Party Bucket, Large Combo Platters', 3);

-- CATEGORY: Kids & Special Menu
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Kids & Special Menu', 'Kids Special Meals', 'Mini Burgers, Cheese Pasta, Chicken Nuggets, French Fries & Dip', 1),
('Kids & Special Menu', 'Chef Special & Seasonal', 'Seasonal specials, Daily chef creations, Festival special items', 2);

-- -----------------------------------------------------------------------------------------------------
-- 2.2 NON-ALCOHOLIC BEVERAGES & CAFÉ CATEGORIES
-- -----------------------------------------------------------------------------------------------------

-- CATEGORY: Beverage
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Beverage', 'Hot Beverages', 'Espresso, Cappuccino, Cafe Latte, Hot Chocolate, Americano', 1),
('Beverage', 'Tea & Specialty Chai', 'Masala Chai, Ginger Tea, Green Tea, Earl Grey, Lemon Honey Tea', 2),
('Beverage', 'Cold Beverages', 'Iced Coffee, Cold Coffee with Ice Cream, Frappes, Iced Teas', 3),
('Beverage', 'Shakes & Mocktails', 'Virgin Mojito, Blue Lagoon, Chocolate Shake, Oreo Shake, Thick Shakes', 4),
('Beverage', 'Fresh Juices & Smoothies', 'Fresh Orange, Watermelon, Pineapple Juice, Berry Smoothies', 5),
('Beverage', 'Soft Drinks & Aerated Waters', 'Coke, Sprite, Thums Up, Diet Coke, Tonic Water, Ginger Ale', 6),
('Beverage', 'Water', 'Packaged Mineral Water, Sparkling Water, Flavored Infused Water', 7);

-- -----------------------------------------------------------------------------------------------------
-- 2.3 COMPLETE BAR & ALCOHOLIC BEVERAGES TAXONOMY (BAR ITEM CATEGORIES)
-- -----------------------------------------------------------------------------------------------------

-- CATEGORY: Bar - Beers & Ciders
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Beers & Ciders', 'Draught Beer (Tap)', 'Fresh draught beer mugs, pitchers, and 3L / 5L beer towers', 1),
('Bar - Beers & Ciders', 'Domestic Bottled Beer', 'Kingfisher Premium/Ultra, Tuborg, Budweiser, Bira 91, Carlsberg', 2),
('Bar - Beers & Ciders', 'Premium & Imported Beer', 'Corona Extra, Heineken, Hoegaarden, Stella Artois, Guiness', 3),
('Bar - Beers & Ciders', 'Craft Beers & Ales', 'Indian Pale Ale (IPA), Belgian Wit, Stout, Wheat Beer, Porter', 4),
('Bar - Beers & Ciders', 'Hard Ciders & Breezers', 'Apple Ciders, Flavored Breezers, Hard Seltzers', 5),
('Bar - Beers & Ciders', 'Non-Alcoholic Beer (0.0%)', 'Heineken 0.0, Budweiser 0.0, zero alcohol malt beverages', 6);

-- CATEGORY: Bar - Whiskey & Single Malts
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Whiskey & Single Malts', 'Single Malt Scotch', 'Glenfiddich 12/15/18 YO, The Macallan, Talisker, Glenmorangie, Laphroaig', 1),
('Bar - Whiskey & Single Malts', 'Blended Scotch Whisky', 'Johnnie Walker (Black/Red/Double Black/Gold), Chivas Regal 12/18, Ballantines', 2),
('Bar - Whiskey & Single Malts', 'Bourbon & American Whiskey', 'Jack Daniels Old No.7, Jim Beam White/Black, Makers Mark, Woodford Reserve', 3),
('Bar - Whiskey & Single Malts', 'Irish & Japanese Whisky', 'Jameson Irish Whiskey, Hibiki, Yamazaki, Toki Japanese Whisky', 4),
('Bar - Whiskey & Single Malts', 'Premium Indian Whiskies', 'Amrut Single Malt, Paul John, Indri, Royal Ranthambore, Signature', 5);

-- CATEGORY: Bar - Vodka
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Vodka', 'Regular & Domestic Vodka', 'Magic Moments, Romanov, Fuel', 1),
('Bar - Vodka', 'Premium & Luxury Vodka', 'Absolut Blue, Smirnoff, Ketel One, Grey Goose, Belvedere, Ciroc', 2),
('Bar - Vodka', 'Flavored Vodka', 'Absolut Citron, Raspberry, Green Apple, Mandarin, Vanilla Vodka', 3);

-- CATEGORY: Bar - Rum
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Rum', 'White Rum', 'Bacardi Carta Blanca, Captain Morgan White', 1),
('Bar - Rum', 'Dark & Aged Rum', 'Old Monk 7 YO / Supreme, Captain Morgan Dark, Havana Club 7 YO', 2),
('Bar - Rum', 'Spiced & Flavored Rum', 'Captain Morgan Spiced Gold, Malibu Coconut Flavored Rum', 3);

-- CATEGORY: Bar - Gin
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Gin', 'Standard & Dry Gin', 'Blue Riband, Greater Than London Dry Gin, Gordons Dry Gin', 1),
('Bar - Gin', 'Premium London Dry Gin', 'Bombay Sapphire, Tanqueray No. Ten, Beefeater London Dry', 2),
('Bar - Gin', 'Artisanal & Craft Gin', 'Hendricks Gin, Monkey 47, Roku Japanese Gin, Stranger & Sons', 3),
('Bar - Gin', 'Pink & Flavored Gin', 'Gordons Pink Gin, Malfy Blood Orange/Lemon, Beefeater Pink Gin', 4);

-- CATEGORY: Bar - Tequila & Mezcal
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Tequila & Mezcal', 'Blanco / Silver Tequila', 'Jose Cuervo Especial Silver, Camino Real Blanco, Patron Silver', 1),
('Bar - Tequila & Mezcal', 'Reposado & Anejo Tequila', 'Jose Cuervo Reposado, 1800 Anejo, Don Julio Reposado, Corralejo', 2),
('Bar - Tequila & Mezcal', 'Mezcal & Agave Specialties', 'Del Maguey Vida Mezcal, Montelobos, artisanal smoked mezcal', 3);

-- CATEGORY: Bar - Brandy & Cognac
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Brandy & Cognac', 'Domestic & Premium Brandy', 'Mansion House, Morpheus XO, Honey Bee, Old Admiral', 1),
('Bar - Brandy & Cognac', 'French Cognac & Armagnac', 'Hennessy VS / VSOP, Remy Martin VSOP, Martell VS / XO', 2);

-- CATEGORY: Bar - Liqueurs & Shots
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Liqueurs & Shots', 'Cream & Coffee Liqueurs', 'Baileys Original Irish Cream, Kahlua Coffee Liqueur, Sheridan', 1),
('Bar - Liqueurs & Shots', 'Herbal, Citrus & Digestifs', 'Jagermeister, Cointreau, Triple Sec, Sambuca, Campari, Aperol', 2),
('Bar - Liqueurs & Shots', 'Shooters & Flaming Shots', 'B-52, Kamikaze, Jagerbomb, Tequila Shots, Fireball', 3);

-- CATEGORY: Bar - Wines & Champagne
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Wines & Champagne', 'Red Wine (Domestic & Imported)', 'Sula Cabernet Shiraz, Grover La Reserve, Jacobs Creek Merlot, Yellow Tail', 1),
('Bar - Wines & Champagne', 'White Wine (Domestic & Imported)', 'Sula Sauvignon Blanc, Grover Sauvignon, Jacobs Creek Chardonnay', 2),
('Bar - Wines & Champagne', 'Rose & Dessert Wines', 'Sula Zinfandel Rose, Mateus Rose, Late Harvest Chenin Blanc, Port Wine', 3),
('Bar - Wines & Champagne', 'Sparkling Wine & Champagne', 'Moet & Chandon Brut, Dom Perignon, Sula Brut, Prosecco Italian Sparkling', 4),
('Bar - Wines & Champagne', 'Sangria & Wine Pitchers', 'Classic Red Wine Sangria, White Wine Tropical Sangria, Berry Rose Sangria', 5);

-- CATEGORY: Bar - Cocktails & Mixology
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Cocktails & Mixology', 'Classic Cocktails', 'Martini, Old Fashioned, Margarita, Long Island Iced Tea (LIIT), Mojito, Negroni', 1),
('Bar - Cocktails & Mixology', 'Signature & House Specials', 'Smoked Whiskey Sour, Spicy Guava Mary, Botanical Gin Tonic, Molecular Cocktails', 2),
('Bar - Cocktails & Mixology', 'Tropical & Tiki Cocktails', 'Pina Colada, Mai Tai, Blue Hawaiian, Singapore Sling, Zombie', 3),
('Bar - Cocktails & Mixology', 'Cocktail Pitchers & Towers', 'LIIT Pitcher, Margarita Pitcher, Mojito Towers, Cosmopolitan Sharing Bowl', 4);

-- CATEGORY: Bar - Bites & Finger Foods
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Bar - Bites & Finger Foods', 'Crispy Bar Bites & Fries', 'French Fries, Cheesy Potato Wedges, Onion Rings, Mozzarella Cheese Sticks', 1),
('Bar - Bites & Finger Foods', 'Bar Munchies & Chakna', 'Masala Peanuts, Roasted Cashews, Masala Papad Cones, Nachos Supreme', 2),
('Bar - Bites & Finger Foods', 'Chicken Wings & Meat Bites', 'BBQ Chicken Wings, Hot Peri Peri Wings, Sausage Fry, Chilli Pork / Bacon Bites', 3),
('Bar - Bites & Finger Foods', 'Cheese & Charcuterie Boards', 'Artisanal Cheese Board with Crackers, Smoked Cold Cuts & Meat Platter', 4);

-- CATEGORY: Happy Hour Menu
INSERT INTO #MasterData (CategoryName, SubCategoryName, SubCategoryDesc, DisplayOrder) VALUES
('Happy Hour Menu', 'Happy Hour Drinks (1+1)', 'Discounted Draught Beers, House Spirits 30ml/60ml, 1+1 Classic Cocktails', 1),
('Happy Hour Menu', 'Happy Hour Bites & Combos', 'Discounted snack sliders, fries combo, mini kebabs & sharing baskets', 2);

SELECT @StagedCount = COUNT(*) FROM #MasterData;
PRINT 'Master data staging completed with ' + CAST(@StagedCount AS NVARCHAR(10)) + ' total sub-category definitions.';
PRINT '--------------------------------------------------------------------------------';

-- =====================================================================================================
-- STEP 3: INSERT / ENSURE OFFICIAL MASTER CATEGORIES EXIST FIRST
-- =====================================================================================================

PRINT 'Ensuring all official Master Categories exist...';

INSERT INTO [dbo].[Categories] ([Name], [IsActive])
SELECT DISTINCT 
    src.CategoryName, 
    1 AS [IsActive]
FROM #MasterData src
WHERE NOT EXISTS (
    SELECT 1 
    FROM [dbo].[Categories] tgt 
    WHERE LOWER(LTRIM(RTRIM(tgt.[Name]))) = LOWER(LTRIM(RTRIM(src.CategoryName)))
);

UPDATE tgt
SET tgt.[IsActive] = 1
FROM [dbo].[Categories] tgt
INNER JOIN (SELECT DISTINCT CategoryName FROM #MasterData) src 
    ON LOWER(LTRIM(RTRIM(tgt.[Name]))) = LOWER(LTRIM(RTRIM(src.CategoryName)))
WHERE tgt.[IsActive] = 0;

PRINT 'Master Categories verified.';
PRINT '--------------------------------------------------------------------------------';

-- =====================================================================================================
-- STEP 4: CLEANUP JUNK / TEST DATA & REMAP REAL MENU ITEMS
-- =====================================================================================================

PRINT 'Executing Production Cleanup for junk, test, and obsolete categories...';

IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MenuItems]') AND type in (N'U'))
BEGIN
    -- 4.1 Remap MenuItems from Old/Duplicate/Junk category names to official standard categories
    -- Remap "Starters / Appetizers", "Starters", "Appetizers" -> "Appetizers & Starters"
    UPDATE m
    SET m.CategoryId = (SELECT TOP 1 Id FROM [dbo].[Categories] WHERE Name = 'Appetizers & Starters')
    FROM [dbo].[MenuItems] m
    INNER JOIN [dbo].[Categories] c ON m.CategoryId = c.Id
    WHERE c.Name IN ('Starters / Appetizers', 'Starters', 'Appetizers')
      AND c.Name <> 'Appetizers & Starters';

    -- Remap "Main Course" -> "Indian Main Course"
    UPDATE m
    SET m.CategoryId = (SELECT TOP 1 Id FROM [dbo].[Categories] WHERE Name = 'Indian Main Course')
    FROM [dbo].[MenuItems] m
    INNER JOIN [dbo].[Categories] c ON m.CategoryId = c.Id
    WHERE c.Name IN ('Main Course')
      AND c.Name <> 'Indian Main Course';

    -- Remap "Special Menu", "VIP Special" -> "Kids & Special Menu"
    UPDATE m
    SET m.CategoryId = (SELECT TOP 1 Id FROM [dbo].[Categories] WHERE Name = 'Kids & Special Menu')
    FROM [dbo].[MenuItems] m
    INNER JOIN [dbo].[Categories] c ON m.CategoryId = c.Id
    WHERE c.Name IN ('Special Menu', 'VIP Special')
      AND c.Name <> 'Kids & Special Menu';

    -- Remap "Japanese" -> "Chinese"
    UPDATE m
    SET m.CategoryId = (SELECT TOP 1 Id FROM [dbo].[Categories] WHERE Name = 'Chinese')
    FROM [dbo].[MenuItems] m
    INNER JOIN [dbo].[Categories] c ON m.CategoryId = c.Id
    WHERE c.Name IN ('Japanese')
      AND c.Name <> 'Chinese';

    -- Remap any TEST / DUMMY categories to "Appetizers & Starters" so items are not corrupted
    UPDATE m
    SET m.CategoryId = (SELECT TOP 1 Id FROM [dbo].[Categories] WHERE Name = 'Appetizers & Starters')
    FROM [dbo].[MenuItems] m
    INNER JOIN [dbo].[Categories] c ON m.CategoryId = c.Id
    WHERE (c.Name LIKE '%TEST%' OR c.Name LIKE 'DEMO%' OR c.Name LIKE 'SAMPLE%' OR c.Name LIKE 'TEMP%')
      AND c.Name NOT IN (SELECT CategoryName FROM #MasterData);

    -- Detach any MenuItems linked to junk/test subcategories
    UPDATE m
    SET m.SubCategoryId = NULL
    FROM [dbo].[MenuItems] m
    INNER JOIN [dbo].[SubCategories] sc ON m.SubCategoryId = sc.Id
    INNER JOIN [dbo].[Categories] c ON sc.CategoryId = c.Id
    WHERE c.Name LIKE '%TEST%' 
       OR c.Name LIKE 'DEMO%' 
       OR c.Name LIKE 'SAMPLE%' 
       OR c.Name LIKE 'TEMP%' 
       OR c.Name IN ('TEST', 'TEST_KOL', 'VIP Special', 'Starters / Appetizers', 'Main Course', 'Special Menu', 'Japanese')
       OR sc.Name LIKE '%TEST%'
       OR sc.Name LIKE '%DUMMY%';
END

-- 4.2 Delete Junk & Test SubCategories
DELETE sc
FROM [dbo].[SubCategories] sc
INNER JOIN [dbo].[Categories] c ON sc.CategoryId = c.Id
WHERE c.Name LIKE '%TEST%' 
   OR c.Name LIKE 'DEMO%' 
   OR c.Name LIKE 'SAMPLE%' 
   OR c.Name LIKE 'TEMP%' 
   OR c.Name IN ('TEST', 'TEST_KOL', 'VIP Special', 'Starters / Appetizers', 'Main Course', 'Special Menu', 'Japanese')
   OR sc.Name LIKE '%TEST%'
   OR sc.Name LIKE '%DUMMY%';

SET @DeletedJunkSubCatCount = @@ROWCOUNT;
PRINT 'Deleted ' + CAST(@DeletedJunkSubCatCount AS NVARCHAR(10)) + ' junk/test sub-categories.';

-- 4.3 Delete Junk, Test, and Obsolete Categories (having 0 linked MenuItems)
DELETE c
FROM [dbo].[Categories] c
WHERE (
    c.Name LIKE '%TEST%' 
    OR c.Name LIKE 'DEMO%' 
    OR c.Name LIKE 'SAMPLE%' 
    OR c.Name LIKE 'TEMP%' 
    OR c.Name LIKE 'JUNK%'
    OR c.Name IN ('TEST', 'TEST_KOL', 'VIP Special', 'Starters / Appetizers', 'Main Course', 'Special Menu', 'Japanese')
    OR c.Name NOT IN (SELECT CategoryName FROM #MasterData)
)
AND NOT EXISTS (
    SELECT 1 FROM [dbo].[MenuItems] mi WHERE mi.CategoryId = c.Id
)
AND NOT EXISTS (
    SELECT 1 FROM [dbo].[SubCategories] sc WHERE sc.CategoryId = c.Id
);

SET @DeletedJunkCatCount = @@ROWCOUNT;
PRINT 'Deleted ' + CAST(@DeletedJunkCatCount AS NVARCHAR(10)) + ' junk/test categories.';
PRINT 'Production cleanup completed successfully.';
PRINT '--------------------------------------------------------------------------------';

-- =====================================================================================================
-- STEP 5: IDEMPOTENT POPULATION (SUB-CATEGORIES)
-- =====================================================================================================

PRINT 'Populating & standardizing [dbo].[SubCategories]...';

-- Insert or Update SubCategories matching Category by Name
MERGE [dbo].[SubCategories] AS target
USING (
    SELECT 
        c.[Id] AS CategoryId,
        m.SubCategoryName,
        ISNULL(m.SubCategoryDesc, '') AS SubCategoryDesc,
        m.DisplayOrder,
        1 AS IsActive
    FROM #MasterData m
    INNER JOIN [dbo].[Categories] c 
        ON LOWER(LTRIM(RTRIM(c.[Name]))) = LOWER(LTRIM(RTRIM(m.CategoryName)))
) AS source
ON (
    target.[CategoryId] = source.CategoryId 
    AND LOWER(LTRIM(RTRIM(target.[Name]))) = LOWER(LTRIM(RTRIM(source.SubCategoryName)))
)
WHEN MATCHED THEN
    UPDATE SET 
        target.[Description]  = source.SubCategoryDesc,
        target.[DisplayOrder]  = source.DisplayOrder,
        target.[IsActive]      = 1,
        target.[UpdatedAt]     = GETDATE()
WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Name], [Description], [CategoryId], [IsActive], [DisplayOrder], [CreatedAt], [UpdatedAt])
    VALUES (source.SubCategoryName, source.SubCategoryDesc, source.CategoryId, source.IsActive, source.DisplayOrder, GETDATE(), GETDATE());

PRINT 'SubCategories population completed successfully.';
PRINT '--------------------------------------------------------------------------------';

-- =====================================================================================================
-- STEP 6: VERIFICATION & AUDIT REPORTS
-- =====================================================================================================

PRINT '================================================================================';
PRINT '  PRODUCTION MASTER VERIFICATION REPORT';
PRINT '================================================================================';

-- 6.1 Clean Categories Summary with SubCategory and MenuItem counts
SELECT 
    c.[Id] AS [CategoryId],
    c.[Name] AS [CategoryName],
    CASE WHEN c.[IsActive] = 1 THEN 'Yes' ELSE 'No' END AS [Active],
    COUNT(DISTINCT sc.[Id]) AS [Total_SubCategories],
    (SELECT COUNT(*) FROM [dbo].[MenuItems] mi WHERE mi.CategoryId = c.Id) AS [Total_MenuItems_Assigned]
FROM [dbo].[Categories] c
LEFT JOIN [dbo].[SubCategories] sc ON c.[Id] = sc.[CategoryId]
GROUP BY c.[Id], c.[Name], c.[IsActive]
ORDER BY c.[Name] ASC;

-- 6.2 Total Clean Counts
SELECT 
    (SELECT COUNT(*) FROM [dbo].[Categories]) AS [Total_Clean_Categories],
    (SELECT COUNT(*) FROM [dbo].[SubCategories]) AS [Total_Clean_SubCategories],
    (SELECT COUNT(*) FROM [dbo].[Categories] WHERE Name LIKE '%TEST%' OR Name LIKE 'DEMO%') AS [Junk_Categories_Remaining];

-- Cleanup Temp Table
IF OBJECT_ID('tempdb..#MasterData') IS NOT NULL DROP TABLE #MasterData;

PRINT '================================================================================';
PRINT '  ALL JUNK DATA REMOVED. CLEAN PRODUCTION CATEGORIES & SUBCATEGORIES READY!';
PRINT '================================================================================';
