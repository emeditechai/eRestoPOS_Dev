-- Update stored procedure to process a payment with GST information
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_ProcessPayment]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_ProcessPayment]
GO

CREATE PROCEDURE [dbo].[usp_ProcessPayment]
    @OrderId INT,
    @PaymentMethodId INT,
    @Amount DECIMAL(18,2),
    @TipAmount DECIMAL(18,2),
    @ReferenceNumber NVARCHAR(100) = NULL,
    @LastFourDigits NVARCHAR(4) = NULL,
    @CardType NVARCHAR(50) = NULL,
    @AuthorizationCode NVARCHAR(50) = NULL,
    @Notes NVARCHAR(500) = NULL,
    @ProcessedBy INT = NULL,
    @ProcessedByName NVARCHAR(100) = NULL,
    -- New GST-related parameters
    @GSTAmount DECIMAL(18,2) = NULL,
    @CGSTAmount DECIMAL(18,2) = NULL,
    @SGSTAmount DECIMAL(18,2) = NULL,
    @DiscAmount DECIMAL(18,2) = NULL,
    @GST_Perc DECIMAL(5,2) = NULL,
    @CGST_Perc DECIMAL(5,2) = NULL,
    @SGST_Perc DECIMAL(5,2) = NULL,
        @Amount_ExclGST DECIMAL(18,2) = NULL,
        @RoundoffAdjustmentAmt DECIMAL(18,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @OrderStatus INT;
    DECLARE @OrderTotal DECIMAL(18,2);
    DECLARE @CurrentlyPaid DECIMAL(18,2);
    DECLARE @PaymentStatus INT = 1; -- Default to approved
    DECLARE @PaymentId INT;
    DECLARE @ErrorMessage NVARCHAR(200);
    
    BEGIN TRY
        BEGIN TRANSACTION;
        
        -- Validate the order
        SELECT @OrderStatus = Status, @OrderTotal = TotalAmount
        FROM Orders
        WHERE Id = @OrderId;
        
        IF @OrderStatus IS NULL
        BEGIN
            SET @ErrorMessage = 'Order not found.';
            RAISERROR(@ErrorMessage, 16, 1);
            RETURN;
        END
        
        IF @OrderStatus = 4 -- Cancelled
        BEGIN
            SET @ErrorMessage = 'Cannot process payment for a cancelled order.';
            RAISERROR(@ErrorMessage, 16, 1);
            RETURN;
        END
        
        IF @OrderStatus = 3 -- Completed
        BEGIN
            SET @ErrorMessage = 'Order is already completed. Additional payments require manager approval.';
            SET @PaymentStatus = 0; -- Set to pending
        END
        
        -- Calculate currently paid amount
        SELECT @CurrentlyPaid = ISNULL(SUM(Amount + TipAmount), 0)
        FROM Payments
        WHERE OrderId = @OrderId AND Status = 1; -- Approved payments only
        
        -- Validate payment amount
        IF (@CurrentlyPaid + @Amount + @TipAmount) > (@OrderTotal * 1.1) -- Allow up to 10% overpayment
        BEGIN
            SET @ErrorMessage = 'Payment amount exceeds order total by more than 10%.';
            THROW 51000, @ErrorMessage, 1;
        END
        
        -- Create payment record with GST information
        INSERT INTO Payments (
            OrderId,
            PaymentMethodId,
            Amount,
            TipAmount,
            Status,
            ReferenceNumber,
            LastFourDigits,
            CardType,
            AuthorizationCode,
            Notes,
            ProcessedBy,
            ProcessedByName,
            GSTAmount,
            CGSTAmount,
            SGSTAmount,
            DiscAmount,
            GST_Perc,
            CGST_Perc,
            SGST_Perc,
                Amount_ExclGST,
                RoundoffAdjustmentAmt,
            CreatedAt,
            UpdatedAt
        )
        VALUES (
            @OrderId,
            @PaymentMethodId,
            @Amount,
            @TipAmount,
            @PaymentStatus,
            @ReferenceNumber,
            @LastFourDigits,
            @CardType,
            @AuthorizationCode,
            @Notes,
            @ProcessedBy,
            @ProcessedByName,
            @GSTAmount,
            @CGSTAmount,
            @SGSTAmount,
            @DiscAmount,
            @GST_Perc,
            @CGST_Perc,
            @SGST_Perc,
            @Amount_ExclGST,
                @RoundoffAdjustmentAmt,
            GETDATE(),
            GETDATE()
        );
        
        SET @PaymentId = SCOPE_IDENTITY();
        
        -- Update order if fully paid
        IF (@CurrentlyPaid + @Amount + @TipAmount) >= @OrderTotal AND @OrderStatus < 3
        BEGIN
            -- Update order status to completed
            UPDATE Orders
            SET Status = 3, -- Completed
                CompletedAt = GETDATE(),
                UpdatedAt = GETDATE(),
                TipAmount = TipAmount + @TipAmount -- Add this payment's tip to order total tip
            WHERE Id = @OrderId;
        END
        ELSE IF @TipAmount > 0
        BEGIN
            -- Update order with additional tip amount
            UPDATE Orders
            SET TipAmount = TipAmount + @TipAmount,
                UpdatedAt = GETDATE()
            WHERE Id = @OrderId;
        END
        
        COMMIT TRANSACTION;
        
        -- Return payment ID and status
        SELECT @PaymentId AS PaymentId, @PaymentStatus AS Status, 'Payment processed successfully.' AS Message;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
            
        -- Return error
        SELECT 0 AS PaymentId, -1 AS Status, ERROR_MESSAGE() AS Message;
    END CATCH;
END
GO

PRINT 'Updated usp_ProcessPayment stored procedure with GST columns successfully.';