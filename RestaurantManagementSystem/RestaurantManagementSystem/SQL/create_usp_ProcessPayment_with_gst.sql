-- Update stored procedure to process payments with GST data
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[usp_ProcessPayment]') AND type in (N'P', N'PC'))
    DROP PROCEDURE [dbo].[usp_ProcessPayment]
GO

CREATE PROCEDURE [dbo].[usp_ProcessPayment]
    @OrderId INT,
    @PaymentMethodId INT,
    @Amount DECIMAL(10,2),
    @TipAmount DECIMAL(10,2) = 0,
    @ReferenceNumber NVARCHAR(100) = NULL,
    @LastFourDigits NVARCHAR(4) = NULL,
    @CardType NVARCHAR(50) = NULL,
    @AuthorizationCode NVARCHAR(50) = NULL,
    @Notes NVARCHAR(500) = NULL,
    @ProcessedBy INT = NULL,
    @ProcessedByName NVARCHAR(100) = NULL,
    @GSTAmount DECIMAL(10,2) = NULL,
    @CGSTAmount DECIMAL(10,2) = NULL,
    @SGSTAmount DECIMAL(10,2) = NULL,
    @DiscAmount DECIMAL(10,2) = NULL,
    @GST_Perc DECIMAL(5,2) = NULL,
    @CGST_Perc DECIMAL(5,2) = NULL,
    @SGST_Perc DECIMAL(5,2) = NULL,
    @Amount_ExclGST DECIMAL(10,2) = NULL
    ,@RoundoffAdjustmentAmt DECIMAL(10,2) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @PaymentId INT;
    DECLARE @PaymentStatus INT;
    DECLARE @Message NVARCHAR(500);
    DECLARE @RequiresApproval BIT;
    
    BEGIN TRY
        -- Validate order exists
        IF NOT EXISTS (SELECT 1 FROM Orders WHERE Id = @OrderId)
        BEGIN
            SELECT 0 AS PaymentId, 0 AS PaymentStatus, 'Order not found' AS Message;
            RETURN;
        END
        
        -- Get payment method details
        SELECT @RequiresApproval = RequiresApproval
        FROM PaymentMethods
        WHERE Id = @PaymentMethodId;
        
        -- Set payment status based on approval requirement
        SET @PaymentStatus = CASE WHEN @RequiresApproval = 1 THEN 0 ELSE 1 END;
        
        -- Insert payment with GST information
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
        
        -- Check if order is fully paid
        DECLARE @TotalPaid DECIMAL(10,2);
        DECLARE @OrderTotal DECIMAL(10,2);
        
    SELECT @TotalPaid = ISNULL(SUM(Amount + TipAmount + ISNULL(RoundoffAdjustmentAmt,0)), 0)
        FROM Payments
        WHERE OrderId = @OrderId AND Status = 1; -- Approved payments only
        
        SELECT @OrderTotal = TotalAmount
        FROM Orders
        WHERE Id = @OrderId;
        
        -- Update order status if fully paid
        IF @TotalPaid >= @OrderTotal
        BEGIN
            UPDATE Orders
            SET Status = CASE WHEN Status < 3 THEN 3 ELSE Status END, -- Set to Completed if not already completed or cancelled
                CompletedAt = CASE WHEN Status < 3 AND CompletedAt IS NULL THEN GETDATE() ELSE CompletedAt END,
                UpdatedAt = GETDATE()
            WHERE Id = @OrderId;
        END
        
        SET @Message = CASE 
            WHEN @PaymentStatus = 1 THEN 'Payment processed successfully'
            ELSE 'Payment saved for approval'
        END;
        
        SELECT @PaymentId AS PaymentId, @PaymentStatus AS PaymentStatus, @Message AS Message;
        
    END TRY
    BEGIN CATCH
        SET @Message = 'Error processing payment: ' + ERROR_MESSAGE();
        SELECT 0 AS PaymentId, -1 AS PaymentStatus, @Message AS Message;
    END CATCH
END
GO

PRINT 'Updated usp_ProcessPayment stored procedure with GST parameters successfully.';