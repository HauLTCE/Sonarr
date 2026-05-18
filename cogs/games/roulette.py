import discord
import random
import asyncio
from utils.economy_helpers import update_wallet, record_gamble

RED_NUMBERS = {1,3,5,7,9,12,14,16,18,19,21,23,25,27,30,32,34,36}
BLACK_NUMBERS = {2,4,6,8,10,11,13,15,17,20,22,24,26,28,29,31,33,35}
GREEN_NUMBERS = {0}

async def start_roulette(cog, ctx, bet: int, choice: str):
    choice = choice.lower()
    
    valid_choices = ['red', 'black', 'odd', 'even', 'high', 'low', '1st', '2nd', '3rd']
    is_number = False
    
    if choice.isdigit() and 0 <= int(choice) <= 36:
        is_number = True
    elif choice not in valid_choices:
        await ctx.send("Invalid choice. Bet on a number (0-36), red/black, odd/even, high/low, or 1st/2nd/3rd.")
        return
        
    cog.active_games.add(ctx.author.id)
    try:
        update_wallet(ctx.author.id, -bet)
        
        embed = discord.Embed(title="🎡 Roulette", color=0x3498DB)
        embed.description = (
            f"Bet: {bet:,} 🪙\n"
            f"You bet: {choice.upper()}\n\n"
            f"The wheel spins..."
        )
        msg = await ctx.send(embed=embed)
        
        await asyncio.sleep(2)
        
        result = random.randint(0, 36)
        
        if result == 0:
            color = "🟢"
        elif result in RED_NUMBERS:
            color = "🔴"
        else:
            color = "⚫"
            
        win = False
        multiplier = 0
        
        if is_number and int(choice) == result:
            win = True
            multiplier = 35
        elif result != 0:
            if choice == 'red' and result in RED_NUMBERS:
                win = True
                multiplier = 2
            elif choice == 'black' and result in BLACK_NUMBERS:
                win = True
                multiplier = 2
            elif choice == 'odd' and result % 2 != 0:
                win = True
                multiplier = 2
            elif choice == 'even' and result % 2 == 0:
                win = True
                multiplier = 2
            elif choice == 'high' and result >= 19:
                win = True
                multiplier = 2
            elif choice == 'low' and result <= 18:
                win = True
                multiplier = 2
            elif choice == '1st' and 1 <= result <= 12:
                win = True
                multiplier = 3
            elif choice == '2nd' and 13 <= result <= 24:
                win = True
                multiplier = 3
            elif choice == '3rd' and 25 <= result <= 36:
                win = True
                multiplier = 3
                
        color_name = "GREEN" if result == 0 else ("RED" if result in RED_NUMBERS else "BLACK")
        
        embed.description = (
            f"Bet: {bet:,} 🪙\n"
            f"You bet: {choice.upper()}\n\n"
            f"The wheel spins...\n"
            f"      {color} {result} — {color_name}!\n\n"
        )
        
        if win:
            payout = bet * multiplier
            update_wallet(ctx.author.id, payout)
            record_gamble(ctx.author.id, bet, payout - bet)
            embed.description += f"✅ You won {payout:,} 🪙!"
            embed.color = 0x2ECC71
        else:
            record_gamble(ctx.author.id, bet, -bet)
            embed.description += f"❌ You lost {bet:,} 🪙."
            embed.color = 0xE74C3C
            
        await msg.edit(embed=embed)
        
    except Exception as e:
        update_wallet(ctx.author.id, bet)
        await ctx.send("An error occurred. Bet refunded.")
        raise e
    finally:
        cog.active_games.discard(ctx.author.id)
